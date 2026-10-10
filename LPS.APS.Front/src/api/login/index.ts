import request from '@/axios'
import type { UserLoginType, UserType } from './types'
import { APS_USE_MOCK } from '@/api/aps-v1/http'
import { apsAuthApi } from '@/api/aps-v1/auth'
import { useUserStoreWithOut } from '@/store/modules/user'

interface RoleParams {
  roleName: string
}

/** 项目原 loginApi（mock 阶段走 /mock/user/login）
 *  - APS_USE_MOCK=true  → 走 mock，行为不变
 *  - APS_USE_MOCK=false → 桥接到 APS 真实后端 /api/auth/login
 *    - 入参体从 { username, password } 改为 { userCode, password }（后端 LoginRequestDto）
 *    - 响应三件套写入 userStore.setAuthBundle(token, refreshToken, expiresAt)
 *    - res.data 映射成 UserType（username=userCode，role=roles[0]）保证 LoginForm 不改
 */
export const loginApi = (data: UserLoginType): Promise<IResponse<UserType>> => {
  if (APS_USE_MOCK) {
    return request.post({ url: '/mock/user/login', data }) as Promise<IResponse<UserType>>
  }
  return (async (): Promise<IResponse<UserType>> => {
    const res = await apsAuthApi.login({
      userCode: data.username,
      password: data.password
    })
    // 写三件套到 userStore（供 axios 拦截器自动注入 Authorization）
    const userStore = useUserStoreWithOut()
    userStore.setAuthBundle(res.accessToken, res.refreshToken, res.expiresAt)
    userStore.setUserInfo({
      username: res.userCode,
      password: '',
      role: (res.roles[0] as string) ?? '',
      roleId: String(res.userId)
    })
    return {
      code: 200,
      data: {
        username: res.userCode,
        password: '',
        role: (res.roles[0] as string) ?? '',
        roleId: String(res.userId)
      }
    } as IResponse<UserType>
  })()
}

export const loginOutApi = (): Promise<void> => {
  if (APS_USE_MOCK) {
    // mock 模式：保留原项目行为
    return request
      .get({ url: '/mock/user/loginOut' })
      .then(() => undefined)
      .catch(() => undefined)
  }
  // 真实模式：调 APS /api/auth/logout（best-effort，失败不阻塞本地清理）
  return apsAuthApi.logout().catch((err) => {
    console.warn('[logout] APS /api/auth/logout 失败，本地状态仍清理:', err)
  })
}

export const getUserListApi = ({ params }: AxiosConfig) => {
  return request.get<{
    code: string
    data: {
      list: UserType[]
      total: number
    }
  }>({ url: '/mock/user/list', params })
}

export const getAdminRoleApi = (
  params: RoleParams
): Promise<IResponse<AppCustomRouteRecordRaw[]>> => {
  // 真实模式：3号位暂未提供服务端路由接口，直接返回空数组让 LoginForm 走静态路由兜底分支
  // 静态路由兜底在 LoginForm.vue:257-262（permissionStore.generateRoutes('static') → addRouters[0]）
  if (!APS_USE_MOCK) {
    return Promise.resolve({ code: 200, message: 'ok', data: [] } as IResponse<
      AppCustomRouteRecordRaw[]
    >)
  }
  return request.get({ url: '/mock/role/list', params })
}

export const getTestRoleApi = (params: RoleParams): Promise<IResponse<string[]>> => {
  if (!APS_USE_MOCK) {
    return Promise.resolve({ code: 200, message: 'ok', data: [] } as IResponse<string[]>)
  }
  return request.get({ url: '/mock/role/list2', params })
}
