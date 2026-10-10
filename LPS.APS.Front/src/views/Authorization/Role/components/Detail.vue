<script setup lang="tsx">
import { PropType, ref, unref, nextTick, computed } from 'vue'
import { Descriptions, DescriptionsSchema } from '@/components/Descriptions'
import { ElTag, ElTree } from 'element-plus'
import { findIndex } from '@/utils'
import { getMenuListApi } from '@/api/menu'
import { getDataScopeDictApi } from '@/api/role'
import type { DataScopeOption } from '@/api/role/types'

const props = defineProps({
  currentRow: {
    type: Object as PropType<any>,
    default: () => undefined
  }
})

const dataScopeDict = ref<{
  factories: DataScopeOption[]
  productFamilies: DataScopeOption[]
  resourceGroups: DataScopeOption[]
}>({
  factories: [],
  productFamilies: [],
  resourceGroups: []
})

const loadDataScopeDict = async () => {
  try {
    const res = await getDataScopeDictApi()
    if (res?.data) {
      dataScopeDict.value = res.data
    }
  } catch (e) {
    console.error('Failed to load datascope dict', e)
  }
}
loadDataScopeDict()

const getLabelsByValues = (options: DataScopeOption[], values: string[]) => {
  return values.map((v) => options.find((o) => o.value === v)?.label || v)
}

const dataScopeInfo = computed(() => {
  const ds = props.currentRow?.dataScope
  if (!ds || ds.type === 'all') return null
  return {
    factories: getLabelsByValues(dataScopeDict.value.factories, ds.factories || []),
    productFamilies: getLabelsByValues(
      dataScopeDict.value.productFamilies,
      ds.productFamilies || []
    ),
    resourceGroups: getLabelsByValues(dataScopeDict.value.resourceGroups, ds.resourceGroups || [])
  }
})

const filterPermissionName = (value: string) => {
  const index = findIndex(unref(currentTreeData)?.permissionList || [], (item) => {
    return item.value === value
  })
  return (unref(currentTreeData)?.permissionList || [])[index].label ?? ''
}

const renderTag = (enable?: boolean) => {
  return <ElTag type={!enable ? 'danger' : 'success'}>{enable ? '启用' : '禁用'}</ElTag>
}

const treeRef = ref<typeof ElTree>()

const currentTreeData = ref()
const nodeClick = (treeData: any) => {
  currentTreeData.value = treeData
}

const treeData = ref<any[]>([])
const getMenuList = async () => {
  const res = await getMenuListApi()
  if (res) {
    treeData.value = res.data.list
    await nextTick()
  }
}
getMenuList()

const detailSchema = ref<DescriptionsSchema[]>([
  {
    field: 'roleName',
    label: '角色名称'
  },
  {
    field: 'status',
    label: '状态',
    slots: {
      default: (data: any) => {
        return renderTag(data.status)
      }
    }
  },
  {
    field: 'remark',
    label: '备注',
    span: 24
  },
  {
    field: 'permissionList',
    label: '菜单分配',
    span: 24,
    slots: {
      default: () => {
        return (
          <>
            <div class="flex w-full">
              <div class="flex-1">
                <ElTree
                  ref={treeRef}
                  node-key="id"
                  props={{ children: 'children', label: 'title' }}
                  highlight-current
                  expand-on-click-node={false}
                  data={treeData.value}
                  onNode-click={nodeClick}
                >
                  {{
                    default: (data) => {
                      return <span>{data?.data?.title}</span>
                    }
                  }}
                </ElTree>
              </div>
              <div class="flex-1">
                {unref(currentTreeData)
                  ? unref(currentTreeData)?.meta?.permission?.map((v: string) => {
                      return <ElTag class="ml-2 mt-2">{filterPermissionName(v)}</ElTag>
                    })
                  : null}
              </div>
            </div>
          </>
        )
      }
    }
  }
])
</script>

<template>
  <Descriptions :schema="detailSchema" :data="currentRow || {}" />

  <!-- 数据范围展示 -->
  <div class="datascope-detail">
    <div class="datascope-detail-title">数据范围</div>
    <div
      v-if="!currentRow?.dataScope || currentRow?.dataScope?.type === 'all'"
      class="datascope-detail-all"
    >
      <ElTag type="success">全部数据</ElTag>
      <span class="datascope-detail-hint">该角色可访问所有工厂、产品族、资源组的数据</span>
    </div>
    <div v-else-if="dataScopeInfo" class="datascope-detail-custom">
      <div v-if="dataScopeInfo.factories.length" class="datascope-detail-row">
        <span class="datascope-detail-label">可见工厂：</span>
        <ElTag v-for="item in dataScopeInfo.factories" :key="item" class="mr-1 mb-1">{{
          item
        }}</ElTag>
      </div>
      <div v-if="dataScopeInfo.productFamilies.length" class="datascope-detail-row">
        <span class="datascope-detail-label">产品族：</span>
        <ElTag
          v-for="item in dataScopeInfo.productFamilies"
          :key="item"
          type="warning"
          class="mr-1 mb-1"
          >{{ item }}</ElTag
        >
      </div>
      <div v-if="dataScopeInfo.resourceGroups.length" class="datascope-detail-row">
        <span class="datascope-detail-label">资源组：</span>
        <ElTag
          v-for="item in dataScopeInfo.resourceGroups"
          :key="item"
          type="info"
          class="mr-1 mb-1"
          >{{ item }}</ElTag
        >
      </div>
    </div>
  </div>
</template>

<style lang="less" scoped>
.datascope-detail {
  padding: 16px;
  margin-top: 20px;
  background: #fafafa;
  border: 1px solid #ebeef5;
  border-radius: 8px;

  .datascope-detail-title {
    padding-bottom: 8px;
    margin-bottom: 12px;
    font-size: 15px;
    font-weight: 600;
    color: #303133;
    border-bottom: 1px solid #ebeef5;
  }

  .datascope-detail-all {
    display: flex;
    align-items: center;
    gap: 12px;

    .datascope-detail-hint {
      font-size: 13px;
      color: #909399;
    }
  }

  .datascope-detail-custom {
    .datascope-detail-row {
      display: flex;
      align-items: flex-start;
      margin-bottom: 12px;
      flex-wrap: wrap;

      &:last-child {
        margin-bottom: 0;
      }

      .datascope-detail-label {
        width: 80px;
        font-size: 14px;
        line-height: 24px;
        color: #606266;
        flex-shrink: 0;
      }
    }
  }
}
</style>
