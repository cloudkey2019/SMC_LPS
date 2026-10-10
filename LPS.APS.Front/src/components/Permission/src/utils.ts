import { useI18n } from '@/hooks/web/useI18n'
import router from '@/router'
import { useUserStoreWithOut } from '@/store/modules/user'

export const hasPermi = (value: string) => {
  const { t } = useI18n()
  const routePerms = (router.currentRoute.value.meta.permission || []) as string[]
  if (!value) {
    throw new Error(t('permission.hasPermission'))
  }
  if (!routePerms.includes(value)) {
    return false
  }
  const userStore = useUserStoreWithOut()
  const userPerms = (userStore.getUserInfo?.permissions || []) as string[]
  if (userPerms.includes('*.*.*')) {
    return true
  }
  return userPerms.includes(value)
}
