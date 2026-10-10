import type { App, Directive, DirectiveBinding } from 'vue'
import { useI18n } from '@/hooks/web/useI18n'
import router from '@/router'
import { useUserStoreWithOut } from '@/store/modules/user'

const { t } = useI18n()

const hasPermission = (value: string): boolean => {
  const routePerms = (router.currentRoute.value.meta.permission || []) as string[]
  if (!value) {
    throw new Error(t('permission.hasPermission'))
  }
  // 条件 1：当前路由声明了该权限
  if (!routePerms.includes(value)) {
    return false
  }
  // 条件 2：当前用户持有该权限（或通配 *.*.*）
  const userStore = useUserStoreWithOut()
  const userPerms = (userStore.getUserInfo?.permissions || []) as string[]
  if (userPerms.includes('*.*.*')) {
    return true
  }
  return userPerms.includes(value)
}
function hasPermi(el: Element, binding: DirectiveBinding) {
  const value = binding.value

  const flag = hasPermission(value)
  if (!flag) {
    el.parentNode?.removeChild(el)
  }
}
const mounted = (el: Element, binding: DirectiveBinding<any>) => {
  hasPermi(el, binding)
}

const permiDirective: Directive = {
  mounted
}

export const setupPermissionDirective = (app: App<Element>) => {
  app.directive('hasPermi', permiDirective)
}

export default permiDirective
