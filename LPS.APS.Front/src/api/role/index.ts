import request from '@/axios'

export const getRoleListApi = () => {
  return request.get({ url: '/mock/role/table' })
}

export const getDataScopeDictApi = () => {
  return request.get({ url: '/mock/role/datascope-dict' })
}
