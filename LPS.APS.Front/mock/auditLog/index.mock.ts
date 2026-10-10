import { SUCCESS_CODE } from '@/constants'

const timeout = 1000

const operators = ['张三', '李四', '王五', '赵六']

const results = ['Success', 'Success', 'Success', 'Success', 'Success', 'Failed', 'Denied']

const mockRecords = [
  {
    operationType: 'version_publish',
    objectType: 'version',
    target: 'V20260525-01',
    detail: '发布生产版本至正式环境'
  },
  {
    operationType: 'version_rollback',
    objectType: 'version',
    target: 'V20260524-02',
    detail: '回滚至上一生效版本V20260523-01'
  },
  {
    operationType: 'approval',
    objectType: 'approval',
    target: 'UF-003',
    detail: '审批通过解冻申请'
  },
  {
    operationType: 'approval',
    objectType: 'approval',
    target: 'UF-005',
    detail: '驳回解冻申请，原因：不符合条件'
  },
  {
    operationType: 'ctp_insert',
    objectType: 'order',
    target: 'CTP-007',
    detail: '插单已确认执行，影响3个订单'
  },
  {
    operationType: 'ctp_insert',
    objectType: 'order',
    target: 'CTP-012',
    detail: '插单评估完成，建议拒绝'
  },
  {
    operationType: 'task_adjust',
    objectType: 'task',
    target: 'T-1024',
    detail: '修改任务排产时间，前移2小时'
  },
  {
    operationType: 'task_adjust',
    objectType: 'task',
    target: 'T-1031',
    detail: '任务拖拽调整，延后1天'
  },
  { operationType: 'unfreeze', objectType: 'task', target: 'T-1055', detail: '提交冻结区解冻请求' },
  { operationType: 'unfreeze', objectType: 'approval', target: 'UF-008', detail: '解冻申请已撤回' },
  {
    operationType: 'role_change',
    objectType: 'role',
    target: '机种计划员',
    detail: '修改角色菜单权限配置'
  },
  {
    operationType: 'role_change',
    objectType: 'role',
    target: 'APS管理员',
    detail: '修改角色数据范围'
  },
  { operationType: 'user_change', objectType: 'user', target: '用户:周七', detail: '新增用户账号' },
  { operationType: 'user_change', objectType: 'user', target: '用户:钱八', detail: '禁用用户账号' },
  {
    operationType: 'schedule_run',
    objectType: 'version',
    target: 'V20260526-DRAFT',
    detail: '发起排程计算，耗时32秒'
  },
  {
    operationType: 'schedule_run',
    objectType: 'version',
    target: 'V20260525-SIM',
    detail: '发起What-if仿真排程'
  },
  {
    operationType: 'config_change',
    objectType: 'config',
    target: '排程策略',
    detail: '修改换型时间矩阵参数'
  },
  {
    operationType: 'config_change',
    objectType: 'config',
    target: '冻结窗口',
    detail: '调整冻结窗口从48h改为72h'
  },
  {
    operationType: 'version_publish',
    objectType: 'version',
    target: 'V20260524-01',
    detail: '发布生产版本至正式环境'
  },
  {
    operationType: 'task_adjust',
    objectType: 'task',
    target: 'T-1060',
    detail: '批量调整5个任务排产顺序'
  }
]

const mockList: any[] = []
for (let i = 0; i < 30; i++) {
  const record = mockRecords[i % mockRecords.length]
  const day = 26 - Math.floor(i / 5)
  const hour = 8 + Math.floor(Math.random() * 10)
  const minute = Math.floor(Math.random() * 60)
  mockList.push({
    id: i + 1,
    operateTime: `2026-05-${day < 10 ? '0' + day : day} ${hour < 10 ? '0' + hour : hour}:${minute < 10 ? '0' + minute : minute}:00`,
    operator: operators[Math.floor(Math.random() * operators.length)],
    operationType: record.operationType,
    objectType: record.objectType,
    target: record.target,
    result: results[Math.floor(Math.random() * results.length)],
    detail: record.detail,
    ip: `192.168.1.${Math.floor(Math.random() * 200) + 10}`
  })
}

// 可变存储：list 接口读取、create 接口写入、模拟后端 DB
const auditLogStore: any[] = [...mockList]
let nextId = auditLogStore.length + 1

const formatNow = () => {
  const d = new Date()
  const pad = (n: number) => (n < 10 ? '0' + n : String(n))
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
}

export default [
  {
    url: '/mock/audit-log/list',
    method: 'get',
    timeout,
    response: (config: any) => {
      const { operator, operationType, result, page = 1, pageSize = 15 } = config.query || {}
      let filtered = [...auditLogStore]
      if (operator) {
        filtered = filtered.filter((item) => item.operator === operator)
      }
      if (operationType) {
        filtered = filtered.filter((item) => item.operationType === operationType)
      }
      if (result) {
        filtered = filtered.filter((item) => item.result === result)
      }
      const total = filtered.length
      const start = (page - 1) * pageSize
      const list = filtered.slice(start, start + pageSize)
      return {
        code: SUCCESS_CODE,
        data: {
          list,
          total
        }
      }
    }
  },
  {
    url: '/mock/audit-log/create',
    method: 'post',
    timeout,
    response: ({ body }: any) => {
      if (!body || !body.operationType || !body.target) {
        return { code: 500, message: '审计日志参数不完整' }
      }
      const record = {
        id: nextId++,
        operateTime: formatNow(),
        operator: body.operator || 'admin',
        operationType: body.operationType,
        objectType: body.objectType || 'config',
        target: body.target,
        result: body.result || 'Success',
        detail: body.detail || '',
        ip: '127.0.0.1'
      }
      auditLogStore.unshift(record)
      return {
        code: SUCCESS_CODE,
        data: record
      }
    }
  }
]
