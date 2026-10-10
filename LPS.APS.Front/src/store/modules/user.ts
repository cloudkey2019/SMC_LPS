import { defineStore } from 'pinia'
import { store } from '../index'
import { UserLoginType, UserType } from '@/api/login/types'
import { ElMessageBox } from 'element-plus'
import { useI18n } from '@/hooks/web/useI18n'
import { loginOutApi } from '@/api/login'
import { useTagsViewStore } from './tagsView'
import router from '@/router'

interface UserState {
  userInfo?: UserType
  tokenKey: string
  token: string
  /** APS 真实后端 refreshToken（lps/LPS.APS.Web AuthController.refresh 需要 access+refresh pair）
   *  - 仅 APS_USE_MOCK=false 时使用；mock 阶段为空
   *  - 401 自动 refresh 由 src/api/aps-v1/http.ts 拦截器触发
   */
  refreshToken?: string
  /** APS 真实后端 accessToken 过期时间（ISO8601，绝对时间）
   *  - 仅供前端显示/决策是否提前刷新；401 仍以服务端判定为准
   */
  expiresAt?: string
  roleRouters?: string[] | AppCustomRouteRecordRaw[]
  rememberMe: boolean
  loginInfo?: UserLoginType
}

export const useUserStore = defineStore('user', {
  state: (): UserState => {
    return {
      userInfo: undefined,
      tokenKey: 'Authorization',
      token: '',
      refreshToken: undefined,
      expiresAt: undefined,
      roleRouters: undefined,
      // 记住我
      rememberMe: true,
      loginInfo: undefined
    }
  },
  getters: {
    getTokenKey(): string {
      return this.tokenKey
    },
    getToken(): string {
      return this.token
    },
    getRefreshToken(): string | undefined {
      return this.refreshToken
    },
    getExpiresAt(): string | undefined {
      return this.expiresAt
    },
    getUserInfo(): UserType | undefined {
      return this.userInfo
    },
    getRoleRouters(): string[] | AppCustomRouteRecordRaw[] | undefined {
      return this.roleRouters
    },
    getRememberMe(): boolean {
      return this.rememberMe
    },
    getLoginInfo(): UserLoginType | undefined {
      return this.loginInfo
    }
  },
  actions: {
    setTokenKey(tokenKey: string) {
      this.tokenKey = tokenKey
    },
    setToken(token: string) {
      this.token = token
    },
    /** APS 真实后端登录 / refresh 后一次性写入三件套 */
    setAuthBundle(token: string, refreshToken: string, expiresAt: string) {
      this.token = token
      this.refreshToken = refreshToken
      this.expiresAt = expiresAt
    },
    setUserInfo(userInfo?: UserType) {
      this.userInfo = userInfo
    },
    setRoleRouters(roleRouters: string[] | AppCustomRouteRecordRaw[]) {
      this.roleRouters = roleRouters
    },
    logoutConfirm() {
      const { t } = useI18n()
      ElMessageBox.confirm(t('common.loginOutMessage'), t('common.reminder'), {
        confirmButtonText: t('common.ok'),
        cancelButtonText: t('common.cancel'),
        type: 'warning'
      })
        .then(async () => {
          // best-effort：服务端 logout 失败不阻塞本地清理（token 过期/网络抖动属常见）
          await loginOutApi()
          this.reset()
        })
        .catch(() => {})
    },
    reset() {
      const tagsViewStore = useTagsViewStore()
      tagsViewStore.delAllViews()
      this.setToken('')
      // APS refreshToken/expiresAt 跟着清，避免下次登录残留旧 refreshToken
      this.refreshToken = undefined
      this.expiresAt = undefined
      this.setUserInfo(undefined)
      this.setRoleRouters([])
      router.replace('/login')
    },
    logout() {
      this.reset()
    },
    setRememberMe(rememberMe: boolean) {
      this.rememberMe = rememberMe
    },
    setLoginInfo(loginInfo: UserLoginType | undefined) {
      this.loginInfo = loginInfo
    }
  },
  persist: true
})

export const useUserStoreWithOut = () => {
  return useUserStore(store)
}
