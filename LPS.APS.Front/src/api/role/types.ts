export interface DataScopeOption {
  id: number
  label: string
  value: string
}

export interface DataScopeConfig {
  type: 'all' | 'custom'
  factories: string[]
  productFamilies: string[]
  resourceGroups: string[]
}

export interface RoleItem {
  id: string
  roleName: string
  role: string
  status: number
  createTime: string
  remark: string
  menu: any[]
  dataScope?: DataScopeConfig
}
