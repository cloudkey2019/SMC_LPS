import router from './router'
import { useTitle } from '@/hooks/web/useTitle'
import { useNProgress } from '@/hooks/web/useNProgress'
import { usePermissionStoreWithOut } from '@/store/modules/permission'
import { usePageLoading } from '@/hooks/web/usePageLoading'
import { NO_REDIRECT_WHITE_LIST } from '@/constants'
import { useUserStoreWithOut } from '@/store/modules/user'
import { useApsAuthStore } from '@/store/modules/aps/auth'
import { useDomainStore } from '@/store/modules/aps/domain'

const { start, done } = useNProgress()

const { loadStart, loadDone } = usePageLoading()

/**
 * APS V1 4号位 — 路由守卫简化版
 *
 * 设计：
 *  - 不接 RBAC（mock/role/index.mock.ts 还是旧版本路由树，新权限码也未对齐）
 *  - 登录成功后第一次导航，从 asyncRouterMap 全量注入 static 路由
 *  - 后续导航走快路径（permissionStore.isAddRouters=true）
 *  - 用 router.hasRoute('ApsOverview') 探测，避免重复注入
 *  - P1-14：登录成功后加载 APS 业务用户信息（角色 / 权限 / 业务范围），路由级 RBAC 守卫依赖此数据
 */
router.beforeEach(async (to, _from, next) => {
  start()
  loadStart()
  const permissionStore = usePermissionStoreWithOut()
  const userStore = useUserStoreWithOut()
  const apsAuth = useApsAuthStore()

  // 未登录：白名单放行，其余跳登录
  if (!userStore.getUserInfo) {
    if (NO_REDIRECT_WHITE_LIST.indexOf(to.path) !== -1) {
      next()
    } else {
      next(`/login?redirect=${to.path}`)
    }
    return
  }

  // 已登录：避免对 /login 重复跳
  if (to.path === '/login') {
    next({ path: '/' })
    return
  }

  // P1-14：首次加载 APS 业务用户信息（角色 / 权限 / 业务范围）
  //  - mock 模式：同步从 MOCK_ROLE_PRESETS 取
  //  - 生产模式：调 /api/auth/me（待 3号位契约）
  //  - 已加载则跳过（loadUserInfo 内部幂等）
  if (!apsAuth.userInfo) {
    await apsAuth.loadUserInfo()
  }

  // v1.2 Domain专项：domain 字典预加载（必须在 auth 之后，依赖 dataScope）
  //  - bootstrap() 内部 catch 静默，幂等，不阻塞首屏
  useDomainStore().bootstrap()

  // 首次进入：从 asyncRouterMap 注入静态路由
  if (!router.hasRoute('ApsOverview')) {
    await permissionStore.generateRoutes('static')
    permissionStore.getAddRouters.forEach((route) => {
      router.addRoute(route.name || '', route as any)
    })
    permissionStore.setIsAddRouters(true)
    // 重新走一次当前路径，让 vue-router 用新注入的路由表解析
    next({ ...to, replace: true })
    return
  }

  // 已注入完毕：直接放行
  next()
})

router.afterEach((to) => {
  useTitle(to?.meta?.title as string)
  done()
  loadDone()
})
