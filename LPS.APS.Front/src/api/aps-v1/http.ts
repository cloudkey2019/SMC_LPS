/**
 * APS V1 4号位 — 独立 axios 实例 + JWT 注入 + 401 自动刷新
 *
 * 设计动机：
 *  - 项目原 @/axios 的 baseURL 由 VITE_API_BASE_PATH 全局控制，改它会污染其它非 APS 接口
 *  - 本实例独立 baseURL（VITE_APS_API_BASE_URL，默认 http://localhost:5163）
 *  - 401 自动 refresh：拦截器检测 accessToken 过期 → 调 /api/auth/refresh → 重放原请求
 *
 * @see 审核报告 §十六.4↔3 + §十七.17（3号位 接管认证）
 * @see lps/LPS.APS.Web/Controllers/AuthController.cs（login / refresh / me / logout）
 *
 * 不修改项目原 src/axios/*（4号位 硬约束：lps/ 下不动；项目 axios 也保持不动以免影响 mock）
 */

import axios, {
  AxiosError,
  type AxiosInstance,
  type AxiosResponse,
  type InternalAxiosRequestConfig
} from 'axios'
import { ElMessage } from 'element-plus'
import type { ApiResponse, IsoDateTime } from './types'
import { isApsSuccessCode } from './types'
import { useUserStoreWithOut } from '@/store/modules/user'

/** APS 后端 baseURL（来自 .env.dev / .env.pro 的 VITE_APS_API_BASE_URL）
 *  - dev 默认 http://localhost:5163（Program.cs http profile）
 *  - pro 由部署侧配置（如 https://aps.example.com）
 */
export const APS_API_BASE_URL = import.meta.env.VITE_APS_API_BASE_URL || 'http://localhost:5163'

/** 业务错误（含 traceId，便于排错） */
export class ApiError extends Error {
  readonly code: number
  readonly traceId?: string
  readonly timestamp?: IsoDateTime

  constructor(message: string, code: number, traceId?: string, timestamp?: IsoDateTime) {
    super(message)
    this.name = 'ApiError'
    this.code = code
    this.traceId = traceId
    this.timestamp = timestamp
  }
}

/** 内部：把 IResponse 强制收敛为 ApiResponse */
function unwrap<T>(raw: unknown): T {
  const anyRaw = raw as { data?: ApiResponse<T> } | undefined
  const payload = (anyRaw?.data ?? raw) as ApiResponse<T> | undefined
  if (!payload) {
    throw new ApiError('响应数据为空', 500)
  }
  if (!isApsSuccessCode(payload.code)) {
    throw new ApiError(
      payload.message || '请求失败',
      payload.code,
      payload.traceId,
      payload.timestamp
    )
  }
  return (payload.data ?? (null as unknown)) as T
}

export interface RequestOptions {
  url: string
  params?: Record<string, unknown>
  data?: unknown
  headers?: Record<string, string>
  responseType?: 'json' | 'blob' | 'arraybuffer' | 'text'
}

/** 标记是否正在 refresh（防止多个 401 同时触发 refresh 风暴） */
let refreshing: Promise<string> | null = null

/** 创建独立 axios 实例（每次新调用都新建，确保 401 重放拿到的实例带新 token） */
function createInstance(): AxiosInstance {
  const instance = axios.create({
    baseURL: APS_API_BASE_URL,
    timeout: 30000
  })

  // 请求拦截：自动注入 JWT
  instance.interceptors.request.use((config: InternalAxiosRequestConfig) => {
    const userStore = useUserStoreWithOut()
    const token = userStore.getToken
    if (token) {
      config.headers = config.headers ?? {}
      ;(config.headers as Record<string, string>)['Authorization'] = `Bearer ${token}`
    }
    return config
  })

  // 响应拦截：401 → 自动 refresh → 重放；5xx / 网络错 → 跳 500
  instance.interceptors.response.use(
    (res: AxiosResponse) => res,
    async (error: AxiosError<ApiResponse<unknown>>) => {
      const original = error.config as InternalAxiosRequestConfig & { _retried?: boolean }
      const status = error.response?.status
      const respData = error.response?.data as ApiResponse<unknown> | undefined
      const isAuthEndpoint =
        original?.url?.includes('/api/auth/login') || original?.url?.includes('/api/auth/refresh')

      // 业务码 401 / HTTP 401 → refresh 后重放（login/refresh 端点不重试避免死循环）
      if ((status === 401 || respData?.code === 401) && !original._retried && !isAuthEndpoint) {
        original._retried = true
        try {
          const newToken = await doRefresh()
          original.headers = original.headers ?? {}
          ;(original.headers as Record<string, string>)['Authorization'] = `Bearer ${newToken}`
          // 用新实例重放（拿干净拦截器链）
          const replay = createInstance()
          return replay.request(original)
        } catch (refreshErr) {
          // refresh 失败 → 跳登录
          const userStore = useUserStoreWithOut()
          userStore.logout()
          ElMessage.error('登录已过期，请重新登录')
          redirectToLogin()
          return Promise.reject(refreshErr)
        }
      }

      // 业务错误：解包成 ApiError 抛给上层
      if (respData && !isApsSuccessCode(respData.code)) {
        const apiErr = new ApiError(
          respData.message || error.message,
          respData.code,
          respData.traceId,
          respData.timestamp
        )
        if (respData.message) ElMessage.error(respData.message)
        return Promise.reject(apiErr)
      }

      // 后端 5xx → 跳 500 页（不弹 toast，避免淹没上下文）
      if (status && status >= 500) {
        redirectToError(500)
        return Promise.reject(error)
      }

      // 网络层错误（断网 / 超时 / DNS）→ 跳 500 页（500 页通用兜底）
      if (!status) {
        redirectToError(500)
        return Promise.reject(error)
      }

      // 其他 4xx（已由上面 respData 处理过）→ 不再 fallback
      ElMessage.error(error.message || '请求失败')
      return Promise.reject(error)
    }
  )

  return instance
}

/** 跳登录页（避免循环依赖，直接用 window.location 防 router 未就绪） */
function redirectToLogin(): void {
  // 当前不在登录页才跳
  if (typeof window !== 'undefined' && !window.location.pathname.startsWith('/login')) {
    const returnUrl = encodeURIComponent(window.location.pathname + window.location.search)
    window.location.href = `/login?redirect=${returnUrl}`
  }
}

/** 跳错误页（避免在 router 未就绪时拿不到 push，用 window.location） */
function redirectToError(code: 403 | 404 | 500): void {
  if (typeof window === 'undefined') return
  const cur = window.location.pathname
  const target = `/${code}`
  if (cur === target) return
  // 500 用 replace 避免返回栈污染；500 是系统级错误，用户点返回会回到错状态
  window.location.replace(target)
}

/** 全局单例（用于普通业务请求） */
const apsAxios = createInstance()

/** 调 /api/auth/refresh 并把新 token 写回 useUserStore
 *  - 串行化：并发 401 共用同一个 promise
 *  - refresh 失败：清 token + reject
 */
async function doRefresh(): Promise<string> {
  if (refreshing) return refreshing
  const userStore = useUserStoreWithOut()
  const refreshToken = (userStore as unknown as { refreshToken?: string }).refreshToken ?? ''
  if (!refreshToken) throw new ApiError('无 refreshToken', 401)

  refreshing = (async () => {
    // 用裸 axios（不注入 Authorization，避免带过期 token 触发再 401）
    const res = await axios.post<
      ApiResponse<{
        accessToken: string
        refreshToken: string
        expiresAt: string
      }>
    >(
      `${APS_API_BASE_URL}/api/auth/refresh`,
      {
        accessToken: userStore.getToken,
        refreshToken
      },
      { timeout: 10000 }
    )
    const payload = res.data
    if (!isApsSuccessCode(payload.code)) {
      throw new ApiError(payload.message || 'refresh 失败', payload.code)
    }
    const data = payload.data!
    userStore.setAuthBundle(data.accessToken, data.refreshToken, data.expiresAt)
    return data.accessToken
  })()

  try {
    return await refreshing
  } finally {
    refreshing = null
  }
}

export const apsHttp = {
  async get<T>(opts: RequestOptions): Promise<T> {
    const res = await apsAxios.get<unknown, AxiosResponse<ApiResponse<T>>>(opts.url, {
      params: opts.params,
      headers: opts.headers,
      responseType: opts.responseType
    })
    return unwrap<T>(res)
  },
  async post<T>(opts: RequestOptions): Promise<T> {
    const res = await apsAxios.post<unknown, AxiosResponse<ApiResponse<T>>>(opts.url, opts.data, {
      params: opts.params,
      headers: opts.headers
    })
    return unwrap<T>(res)
  },
  async put<T>(opts: RequestOptions): Promise<T> {
    const res = await apsAxios.put<unknown, AxiosResponse<ApiResponse<T>>>(opts.url, opts.data, {
      params: opts.params,
      headers: opts.headers
    })
    return unwrap<T>(res)
  },
  async delete<T>(opts: RequestOptions): Promise<T> {
    const res = await apsAxios.delete<unknown, AxiosResponse<ApiResponse<T>>>(opts.url, {
      params: opts.params,
      headers: opts.headers,
      data: opts.data,
      responseType: opts.responseType
    })
    return unwrap<T>(res)
  }
}

/** 业务路径前缀 */
export const APS_API_PREFIX = '/api'

/** 是否启用 mock（来自 .env / .env.local） */
export const APS_USE_MOCK = import.meta.env.VITE_USE_MOCK === 'true'

/** 暴露给测试 / seed:dev 脚本使用（直接拿 axios 实例绕过 unwrap） */
export const apsAxiosRaw = apsAxios
