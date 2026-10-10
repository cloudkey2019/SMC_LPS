<script setup lang="tsx">
import { Form, FormSchema } from '@/components/Form'
import { useForm } from '@/hooks/web/useForm'
import { PropType, reactive, watch, ref, unref, nextTick } from 'vue'
import { useValidator } from '@/hooks/web/useValidator'
import { useI18n } from '@/hooks/web/useI18n'
import {
  ElTree,
  ElCheckboxGroup,
  ElCheckbox,
  ElRadioGroup,
  ElRadio,
  ElSelect,
  ElOption,
  ElAlert
} from 'element-plus'
import { getMenuListApi } from '@/api/menu'
import { getDataScopeDictApi } from '@/api/role'
import { filter, eachTree } from '@/utils/tree'
import { findIndex } from '@/utils'
import type { DataScopeConfig, DataScopeOption } from '@/api/role/types'

const { t } = useI18n()

const { required } = useValidator()

const props = defineProps({
  currentRow: {
    type: Object as PropType<any>,
    default: () => null
  }
})

const treeRef = ref<typeof ElTree>()

const dataScope = reactive<DataScopeConfig>({
  type: 'all',
  factories: [],
  productFamilies: [],
  resourceGroups: []
})

const dataScopeDict = reactive<{
  factories: DataScopeOption[]
  productFamilies: DataScopeOption[]
  resourceGroups: DataScopeOption[]
}>({
  factories: [],
  productFamilies: [],
  resourceGroups: []
})

const formSchema = ref<FormSchema[]>([
  {
    field: 'roleName',
    label: t('role.roleName'),
    component: 'Input'
  },
  {
    field: 'status',
    label: t('menu.status'),
    component: 'Select',
    componentProps: {
      options: [
        {
          label: t('userDemo.disable'),
          value: 0
        },
        {
          label: t('userDemo.enable'),
          value: 1
        }
      ]
    }
  },
  {
    field: 'menu',
    label: t('role.menu'),
    colProps: {
      span: 24
    },
    formItemProps: {
      slots: {
        default: () => {
          return (
            <>
              <div class="flex w-full">
                <div class="flex-1">
                  <ElTree
                    ref={treeRef}
                    show-checkbox
                    node-key="id"
                    highlight-current
                    check-strictly
                    expand-on-click-node={false}
                    data={treeData.value}
                    onNode-click={nodeClick}
                  >
                    {{
                      default: (data) => {
                        return <span>{data.data.meta.title}</span>
                      }
                    }}
                  </ElTree>
                </div>
                <div class="flex-1">
                  {unref(currentTreeData) && unref(currentTreeData)?.permissionList ? (
                    <ElCheckboxGroup v-model={unref(currentTreeData).meta.permission}>
                      {unref(currentTreeData)?.permissionList.map((v: any) => {
                        return <ElCheckbox label={v.value}>{v.label}</ElCheckbox>
                      })}
                    </ElCheckboxGroup>
                  ) : null}
                </div>
              </div>
            </>
          )
        }
      }
    }
  }
])

const currentTreeData = ref()
const nodeClick = (treeData: any) => {
  currentTreeData.value = treeData
}

const rules = reactive({
  roleName: [required()],
  role: [required()],
  status: [required()]
})

const { formRegister, formMethods } = useForm()
const { setValues, getFormData, getElFormExpose } = formMethods

const treeData = ref([])
const getMenuList = async () => {
  const res = await getMenuListApi()
  if (res) {
    treeData.value = res.data.list
    if (!props.currentRow) return
    await nextTick()
    const checked: any[] = []
    eachTree(props.currentRow.menu, (v) => {
      checked.push({
        id: v.id,
        permission: v.meta?.permission || []
      })
    })
    eachTree(treeData.value, (v) => {
      const index = findIndex(checked, (item) => {
        return item.id === v.id
      })
      if (index > -1) {
        const meta = { ...(v.meta || {}) }
        meta.permission = checked[index].permission
        v.meta = meta
      }
    })
    for (const item of checked) {
      unref(treeRef)?.setChecked(item.id, true, false)
    }
  }
}
getMenuList()

const loadDataScopeDict = async () => {
  try {
    const res = await getDataScopeDictApi()
    if (res?.data) {
      dataScopeDict.factories = res.data.factories || []
      dataScopeDict.productFamilies = res.data.productFamilies || []
      dataScopeDict.resourceGroups = res.data.resourceGroups || []
    }
  } catch (e) {
    console.error('Failed to load datascope dict', e)
  }
}
loadDataScopeDict()

const resetDataScope = () => {
  dataScope.type = 'all'
  dataScope.factories = []
  dataScope.productFamilies = []
  dataScope.resourceGroups = []
}

const submit = async () => {
  const elForm = await getElFormExpose()
  const valid = await elForm?.validate().catch((err) => {
    console.log(err)
  })
  if (valid) {
    const formData = await getFormData()
    const checkedKeys = unref(treeRef)?.getCheckedKeys() || []
    const data = filter(unref(treeData), (item: any) => {
      return checkedKeys.includes(item.id)
    })
    formData.menu = data || []
    formData.dataScope = {
      type: dataScope.type,
      factories: [...dataScope.factories],
      productFamilies: [...dataScope.productFamilies],
      resourceGroups: [...dataScope.resourceGroups]
    }
    if (formData.dataScope.type === 'all') {
      formData.dataScope.factories = []
      formData.dataScope.productFamilies = []
      formData.dataScope.resourceGroups = []
    }
    console.log(formData)
    return formData
  }
}

watch(
  () => props.currentRow,
  (currentRow) => {
    if (!currentRow) {
      resetDataScope()
      return
    }
    setValues(currentRow)
    if (currentRow.dataScope) {
      dataScope.type = currentRow.dataScope.type || 'all'
      dataScope.factories = currentRow.dataScope.factories || []
      dataScope.productFamilies = currentRow.dataScope.productFamilies || []
      dataScope.resourceGroups = currentRow.dataScope.resourceGroups || []
    } else {
      resetDataScope()
    }
  },
  {
    deep: true,
    immediate: true
  }
)

defineExpose({
  submit
})
</script>

<template>
  <Form :rules="rules" @register="formRegister" :schema="formSchema" />

  <!-- 数据范围配置 -->
  <div class="datascope-section">
    <div class="datascope-title">数据范围配置</div>
    <ElAlert
      title="数据范围决定该角色下的用户可以查看和操作哪些工厂、产品族、资源组的排产数据"
      type="info"
      show-icon
      :closable="false"
      style="margin-bottom: 16px"
    />
    <div class="datascope-form">
      <div class="datascope-item">
        <label class="datascope-label">范围类型</label>
        <ElRadioGroup v-model="dataScope.type">
          <ElRadio value="all">全部数据</ElRadio>
          <ElRadio value="custom">自定义范围</ElRadio>
        </ElRadioGroup>
      </div>

      <template v-if="dataScope.type === 'custom'">
        <div class="datascope-item">
          <label class="datascope-label">可见工厂</label>
          <ElSelect
            v-model="dataScope.factories"
            multiple
            collapse-tags
            collapse-tags-tooltip
            placeholder="请选择可见工厂"
            style="width: 100%"
          >
            <ElOption
              v-for="item in dataScopeDict.factories"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </ElSelect>
        </div>
        <div class="datascope-item">
          <label class="datascope-label">产品族</label>
          <ElSelect
            v-model="dataScope.productFamilies"
            multiple
            collapse-tags
            collapse-tags-tooltip
            placeholder="请选择可见产品族"
            style="width: 100%"
          >
            <ElOption
              v-for="item in dataScopeDict.productFamilies"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </ElSelect>
        </div>
        <div class="datascope-item">
          <label class="datascope-label">资源组</label>
          <ElSelect
            v-model="dataScope.resourceGroups"
            multiple
            collapse-tags
            collapse-tags-tooltip
            placeholder="请选择可见资源组"
            style="width: 100%"
          >
            <ElOption
              v-for="item in dataScopeDict.resourceGroups"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </ElSelect>
        </div>
      </template>
    </div>
  </div>
</template>

<style lang="less" scoped>
.datascope-section {
  padding: 16px;
  margin-top: 20px;
  background: #fafafa;
  border: 1px solid #ebeef5;
  border-radius: 8px;

  .datascope-title {
    padding-bottom: 8px;
    margin-bottom: 12px;
    font-size: 15px;
    font-weight: 600;
    color: #303133;
    border-bottom: 1px solid #ebeef5;
  }

  .datascope-form {
    .datascope-item {
      display: flex;
      align-items: center;
      margin-bottom: 16px;

      &:last-child {
        margin-bottom: 0;
      }

      .datascope-label {
        width: 80px;
        font-size: 14px;
        color: #606266;
        flex-shrink: 0;
      }
    }
  }
}
</style>
