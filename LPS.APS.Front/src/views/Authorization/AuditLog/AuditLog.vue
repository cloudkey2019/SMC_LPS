<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import {
  ElCard,
  ElForm,
  ElFormItem,
  ElSelect,
  ElOption,
  ElDatePicker,
  ElButton,
  ElTable,
  ElTableColumn,
  ElTag,
  ElPagination
} from 'element-plus'
import { getAuditLogListApi } from '@/api/auditLog'
import type { AuditLogItem, AuditLogQuery } from '@/api/auditLog/types'

const loading = ref(false)
const tableData = ref<AuditLogItem[]>([])
const total = ref(0)

const query = reactive<AuditLogQuery>({
  operator: '',
  operationType: '',
  result: '',
  dateRange: [],
  page: 1,
  pageSize: 15
})

const operationTypeOptions = [
  { label: '版本发布', value: 'version_publish' },
  { label: '版本回滚', value: 'version_rollback' },
  { label: '审批操作', value: 'approval' },
  { label: 'CTP插单', value: 'ctp_insert' },
  { label: '任务调整', value: 'task_adjust' },
  { label: '解冻申请', value: 'unfreeze' },
  { label: '角色变更', value: 'role_change' },
  { label: '用户变更', value: 'user_change' },
  { label: '发起排程', value: 'schedule_run' },
  { label: '修改配置', value: 'config_change' }
]

const resultOptions = [
  { label: '成功', value: 'Success' },
  { label: '失败', value: 'Failed' },
  { label: '拒绝', value: 'Denied' }
]

const operatorOptions = [
  { label: '张三', value: '张三' },
  { label: '李四', value: '李四' },
  { label: '王五', value: '王五' },
  { label: '赵六', value: '赵六' }
]

const getTypeTagType = (type: string): 'primary' | 'success' | 'warning' | 'danger' | 'info' => {
  const map: Record<string, 'primary' | 'success' | 'warning' | 'danger' | 'info'> = {
    version_publish: 'success',
    version_rollback: 'warning',
    approval: 'primary',
    ctp_insert: 'info',
    task_adjust: 'info',
    unfreeze: 'warning',
    role_change: 'danger',
    user_change: 'danger',
    schedule_run: 'success',
    config_change: 'primary'
  }
  return map[type] || 'info'
}

const getTypeLabel = (type: string): string => {
  const item = operationTypeOptions.find((o) => o.value === type)
  return item?.label || type
}

const getObjectTypeLabel = (type: string): string => {
  const map: Record<string, string> = {
    version: '计划版本',
    task: '排产任务',
    order: '订单',
    role: '角色',
    user: '用户',
    config: '系统配置',
    approval: '审批单'
  }
  return map[type] || type
}

const getResultTagType = (result: string): 'success' | 'danger' | 'warning' => {
  const map: Record<string, 'success' | 'danger' | 'warning'> = {
    Success: 'success',
    Failed: 'danger',
    Denied: 'warning'
  }
  return map[result] || 'success'
}

const getResultLabel = (result: string): string => {
  const map: Record<string, string> = {
    Success: '成功',
    Failed: '失败',
    Denied: '拒绝'
  }
  return map[result] || result
}

const fetchData = async () => {
  loading.value = true
  try {
    const res = await getAuditLogListApi({
      operator: query.operator,
      operationType: query.operationType,
      result: query.result,
      startTime: query.dateRange?.[0] || '',
      endTime: query.dateRange?.[1] || '',
      page: query.page,
      pageSize: query.pageSize
    })
    if (res?.data) {
      tableData.value = res.data.list || []
      total.value = res.data.total || 0
    }
  } catch {
    tableData.value = []
    total.value = 0
  } finally {
    loading.value = false
  }
}

const handleSearch = () => {
  query.page = 1
  fetchData()
}

const handleReset = () => {
  query.operator = ''
  query.operationType = ''
  query.result = ''
  query.dateRange = []
  query.page = 1
  fetchData()
}

const handlePageChange = (page: number) => {
  query.page = page
  fetchData()
}

onMounted(fetchData)
</script>

<template>
  <div class="audit-log-page">
    <!-- 筛选栏 -->
    <ElCard shadow="never" class="filter-card">
      <ElForm :inline="true" :model="query">
        <ElFormItem label="操作人">
          <ElSelect v-model="query.operator" placeholder="全部" clearable style="width: 130px">
            <ElOption
              v-for="item in operatorOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="操作类型">
          <ElSelect v-model="query.operationType" placeholder="全部" clearable style="width: 140px">
            <ElOption
              v-for="item in operationTypeOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="操作结果">
          <ElSelect v-model="query.result" placeholder="全部" clearable style="width: 120px">
            <ElOption
              v-for="item in resultOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="时间范围">
          <ElDatePicker
            v-model="query.dateRange"
            type="daterange"
            range-separator="至"
            start-placeholder="开始日期"
            end-placeholder="结束日期"
            value-format="YYYY-MM-DD"
            style="width: 260px"
          />
        </ElFormItem>
        <ElFormItem>
          <ElButton type="primary" @click="handleSearch">查询</ElButton>
          <ElButton @click="handleReset">重置</ElButton>
        </ElFormItem>
      </ElForm>
    </ElCard>

    <!-- 数据表格 -->
    <ElCard shadow="never" class="table-card">
      <ElTable :data="tableData" v-loading="loading" border stripe>
        <ElTableColumn type="index" label="序号" width="60" align="center" />
        <ElTableColumn prop="operateTime" label="操作时间" width="170" />
        <ElTableColumn prop="operator" label="操作人" width="80" />
        <ElTableColumn prop="operationType" label="操作类型" width="110" align="center">
          <template #default="{ row }">
            <ElTag :type="getTypeTagType(row.operationType)" size="small">
              {{ getTypeLabel(row.operationType) }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="objectType" label="对象类型" width="100" align="center">
          <template #default="{ row }">
            {{ getObjectTypeLabel(row.objectType) }}
          </template>
        </ElTableColumn>
        <ElTableColumn prop="target" label="操作对象" width="140" />
        <ElTableColumn prop="result" label="操作结果" width="90" align="center">
          <template #default="{ row }">
            <ElTag :type="getResultTagType(row.result)" size="small">
              {{ getResultLabel(row.result) }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="detail" label="详情" show-overflow-tooltip />
        <ElTableColumn prop="ip" label="IP地址" width="130" />
      </ElTable>

      <div class="pagination-wrap">
        <ElPagination
          v-model:current-page="query.page"
          :page-size="query.pageSize"
          :total="total"
          layout="total, prev, pager, next"
          @current-change="handlePageChange"
        />
      </div>
    </ElCard>
  </div>
</template>

<style lang="less" scoped>
.audit-log-page {
  padding: 16px;

  .filter-card {
    margin-bottom: 16px;
    border-radius: 8px;

    :deep(.el-card__body) {
      padding: 16px 20px 0;
    }
  }

  .table-card {
    border-radius: 8px;
  }

  .pagination-wrap {
    display: flex;
    justify-content: flex-end;
    margin-top: 16px;
  }
}
</style>
