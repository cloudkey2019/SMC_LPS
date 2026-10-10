我把这三份内容按 **2026-09-13 最新冻结基线**重新交叉核对了。结论是：5号位这次发现的问题是有价值的，而且《9月12日执行清单》里确实混入了几项**前端/联调侧自行扩展、但没有冻结依据的实现要求**，不能直接照单开发。3号位关于 `IDataScopeService` 已提供这一点可以接受，但它只解决了“权限服务怎么调用”，**没有解决不同业务对象到底按哪个Factory/Department/Domain归属的问题**。  

## 一、先裁定《5号位本周执行清单》：不是全部有效

### 1. Manual ETA写端加鉴权：方向正确，但不能把所有权限粗暴合并

Manual ETA由5号位维护，后端必须做功能权限和业务范围校验，这与最新5号位实施包一致。最新冻结还明确要求5号位正确校验Factory、ProductFamily、Department、Domain，浏览器篡改参数不得越权。

但是，执行清单里这种思路：

> 先统一拿 `PlanView` 堵住所有查询端点

只能作为临时安全修复，**不能作为最终冻结实现**。

正式实现必须按业务动作使用对应权限。例如：

* 普通排产查询 → `aps.plan.view`
* Manual ETA查看 → Manual ETA查看权限
* Manual ETA编辑 → Manual ETA编辑权限
* Manual ETA取消 → Manual ETA取消权限
* Demand Protection查看 → Demand Protection查看权限
* Demand Protection释放 → Demand Protection释放权限

不能最后把11个Controller都永久归成 `aps.plan.view`。

---

### 2. Demand Protection Release：端点要补，但清单里的实现方式有一处严重错误

5号位补：

```text
POST /demand-protection/release
```

这个方向正确。

但执行清单写成：

```text
5号位
→ IDemandSupplyHardLockRepository.ReleaseLocksAsync(...)
```

**不允许这样做。**

当前冻结路径明确是：

```text
4号位
→ 5号位API
→ 权限/Scope校验
→ 2号位 Demand Protection Application Service
→ 2号位实际释放
```

5号位不得自己修改Lock、Allocation，也不得直接操作Demand Protection核心表。2号位仍拥有保护数量的实际创建、消费、释放和最终业务资格判断。 

因此这里正式改成：

```text
5号位：
认证
+ Permission
+ Scope
+ 请求格式校验
        ↓
2号位 Application Service：
DemandKey/Lock关系校验
+ 是否允许释放
+ 实际释放
+ 数量/Lock/Allocation一致性
```

至于清单里写的：

> `reason.Length >= 5`

目前我没有在最新冻结文档中找到这一条业务规则。

**不要因为前端DTO或执行清单写了5个字，就把它升级成冻结业务红线。** 可以有“Reason必填”之类接口校验，但长度规则若要成为正式业务规则，需要单独有接口契约依据。

---

### 3. `ProcurementManualEtaOverride.DepartmentCode`：正式撤销，不新增

这一项5号位核对得对。

最新v5.1.6字段说明中的 `ProcurementManualEtaOverride` 只有：

`PONo / LineNo / MaterialId / MaterialCode / ReceivingWarehouse / ManualEta / IsActive / UpdatedBy / UpdatedAt / CreatedBy / CreatedAt / Remark`

**没有 `DepartmentCode`。** 

最新DDL v5.1.6也没有该字段，而且v5.1.5以后明确要求“不引入未冻结字段和关系”。

因此正式裁定：

> **不增加 `ProcurementManualEtaOverride.DepartmentCode`。**

执行清单中引用的“v1.2 §十七.17.2新增DepartmentCode”是错误引用，应撤销。

同时：

> 不能因为Business Scope支持Department，就推导出所有业务实体都必须持久化DepartmentCode。

Business Scope支持：

* Factory
* ProductFamily
* Department / ProductionDepartment
* Domain

是权限体系的**可用维度集合**，不是要求每张业务表都必须拥有四个字段。

因此4号位前端先行加入的Manual ETA `DepartmentCode` DTO也应退出正式契约，而不是反向要求冻结DDL配合前端。

---

### 4. Scope校验不需要再等待3号位新增 `EnsureInScopeAsync`

3号位这次回执是成立的：

现有正式用法：

```text
ResolveScopeAsync(userId)
→ DataScopeContext
→ Allows(...)
→ GetValues(...)
```

已经足够。

所以执行清单中：

> “等待3号位补 EnsureInScopeAsync”

这一前提已经失效。

我同意3号位的两个安全原则：

* `userId` 必须从后端身份声明（Claim）取得，不能相信请求参数；
* 不能只校验用户主动传入的筛选条件，**空参查询也必须按照授权Scope限制结果集**。

这和冻结的数据架构完全一致：Scope必须尽早下推到5号位Query、2号位Query或数据库查询条件，禁止全量结果返回浏览器后过滤。

但3号位说“阻塞全部解除”需要修正为：

> **Scope技术服务阻塞解除；各业务对象的Scope归属映射仍需以下Q1～Q6裁决。**

---

# 二、对5号位 Q1～Q6正式裁定

## Q1：Manual ETA的DepartmentCode

### 裁定

**不增加DepartmentCode。**

因此四个问题统一回答：

1. 没有当前冻结依据，“v1.2 §17.2”的引用属于错误；
2. 不需要选择 `ProductionDepartment.DeptCode` 作为该表字段；
3. 不修改v5.1.6 DDL和字段说明增加该字段；
4. Manual ETA不能为了满足Department Scope而人为制造Department归属。

`Department / ProductionDepartment`仍然是有效Scope类型，但只对**能够从权威业务对象获得ProductionDepartment归属的业务数据**使用。最新3号位实施包也只是定义Department Scope为 `Department / ProductionDepartment`，并没有规定Manual ETA必须有Department。

### Manual ETA应该怎样做Scope？

Manual ETA应通过它所指向的**采购事实**进行授权关联，而不是在Override表复制Department。

即：

```text
Manual ETA Override
PONo + LineNo + Material + ReceivingWarehouse
        ↓
对应采购事实
        ↓
已有的Factory / Material / ProductFamily等权威业务范围
```

能可靠映射哪个Scope，就校验哪个Scope。

**不得为了权限方便给事实表随意补DepartmentCode。**

---

## Q2：PeggingTrace按哪个Factory过滤？

### 裁定

> **Factory Scope以需求侧工厂（DemandFactoryCode）作为该条Pegging关系的主归属。**

不要要求：

```text
DemandFactoryCode在权限内
AND
SupplyFactoryCode也在权限内
```

否则跨厂Pegging会被人为切断，用户明明有权查看自己工厂的Demand，却看不到“这份需求由哪个跨厂Supply承接”，Supply/Pegging Trace就失去业务意义。

最新字段本身已经明确区分：

* `DemandFactoryCode`：需求工厂
* `SupplyFactoryCode`：供给工厂。

因此查询规则：

```text
Pegging Trace主对象 = Demand
Factory Scope = DemandFactoryCode
```

供给工厂属于该Demand的追溯信息。

不能反过来因为用户拥有某个SupplyFactory权限，就让他看到另一个无权限需求工厂的全部Demand。

也就是说，不采用“Demand或Supply任一匹配就展示”。

---

## Q3：Explanation的Factory / Domain怎么映射？

### 裁定

**归2号位Explanation Query契约负责提供/过滤，5号位不得自己猜。**

最新冻结链路明确：

```text
4
→ 5
→ 2号位 Explanation Query
```

而且5号位不得重算正式ReasonCode。

因此，应扩展/确认2号位Explanation Query的**查询上下文**，而不是要求5号位自己通过若干表Join推断。

映射原则：

* Order/Demand类Explanation → 使用该需求所属Factory / Domain；
* FinalTask类Explanation → 使用该Task所属PlanVersion/Domain及业务Factory；
* Domain类Explanation → 直接使用DomainKey；
* Stage/其它对象 → 从2号位拥有的运行对象关系解析。

正式建议契约是：

```text
5号位传入：
AllowedFactories
AllowedProductFamilies
AllowedDepartments
AllowedDomains

2号位Explanation Query：
只返回合法Scope中的Explanation
```

而不是：

```text
2号位返回全部
→ 5号位事后猜Factory/Domain
```

这同时符合“Scope尽早下推”的冻结原则。

---

## Q4：PI Position按哪个Factory？

### 裁定

> **以PI本身所属生产工厂（PI FactoryId / FactoryCode）作为PI Position的Factory Scope归属。**

不是根据某条Position当前物理位置变化Factory Scope。

原因很重要：

PI Position是：

> 一个Production Instruction内部的互斥位置切片。

不是独立Supply，更不是每个Position都成为新的业务主对象。PI Position接口输入本身就以 `ProductionInstructionNo + MaterialId + FactoryId` 为PI身份。

因此：

```text
PI Factory在用户Scope内
→ 可以查看这个PI的完整Position闭合
```

即使其中某个Position：

```text
PositionType = INTERPLANT_IN_TRANSIT
```

也不能因为它正在跨厂途中，把这一条Position改归目标工厂，然后造成一个PI的Position被权限切成半截。

否则：

```text
Σ PositionQty = ERP RemainingQty
```

在页面查询层都会被破坏。

所以5号位提出的三个选择中，应采用：

> **PI源/所属生产工厂在Scope内，则允许查看该PI完整Position集合。**

但这里更准确叫“PI所属工厂”，不要把它写成新的“跨厂源厂算法”。

---

## Q5：BusinessFactIssue的Factory Scope，NULL怎么办？

### 裁定

BusinessFactIssue仍然必须受Scope控制，但：

> **FactoryCode=NULL绝不等于“任何用户都可以看”。**

否则只要Issue不挂Factory，就可以绕过业务范围权限，这会形成明显权限漏洞。

正式采用三级处理：

```text
① Issue能够明确关联Factory
→ 按Factory Scope过滤

② Factory为空，但能通过Domain / ProductFamily / Department /
   Workset业务对象等权威关系确定Scope
→ 按可确定的权威Scope过滤

③ 真正属于系统级/全局Issue，无法归属于任何业务范围
→ 仅Global Scope用户可见
```

不能采用：

```text
FactoryCode IS NULL → 全部放行
```

也不要采用：

```text
FactoryCode IS NULL → 全部删除
```

前者越权，后者会把真正的全局数据质量问题彻底隐藏。

对于BOM边等当前确实没有Factory归属的Issue，如果能够通过本次Workset/Domain上下文取得业务归属，就使用那个权威归属；如果确实无法归属，则作为Global Issue。

这与权限模型的“Global + Factory/ProductFamily/Department/Domain”是一致的。

---

## Q6：G8 DomainStatus由谁做Domain Scope过滤？

这一项需要修正我们前一次旧口径。

按照**当前9月13日最新冻结文档**：

G4/G7/G8/Schedule普通查询的正式目标已经明确为：

```text
旧：
4 → 3 → 普通结果

新：
4 → 5 → 2
```

2号位实施包也明确要求给5号位提供：

* G4
* G7
* G8
* Schedule

普通查询所需真实运行事实。 

因此最终裁定：

> **G8必须做Domain Scope过滤；正式长期路径按4→5→2。**

具体：

```text
5号位
读取3号位认证/Scope上下文
        ↓
取得AllowedDomainKeys
        ↓
传给2号位G8/DomainStatus Query
        ↓
2号位只返回AllowedDomainKeys中的DomainStatus
```

5号位可以做最终防御性校验，但主过滤应尽早下推给查询服务。

当前已有：

```text
3号位 IRunLifecycleService
```

可以作为迁移期兼容实现，但**不应继续成为5号位长期G8真源依赖**，因为最新冻结已明确“普通结果查询迁5，真实结果由2号位提供”；3号位继续负责的是Run/Candidate**治理**，不是普通结果Gateway。

---

# 三、给5号位的最终执行结论

| 项目                             | 正式结论                                                   |
| ------------------------------ | ------------------------------------------------------ |
| `IDataScopeService`            | 已有，直接使用 `ResolveScopeAsync + DataScopeContext`，不再等待新接口 |
| 空参查询                           | 必须按授权Scope收窄，不能返回全域                                    |
| Manual ETA `DepartmentCode`    | **撤销，不加DTO正式字段、不改DDL**                                 |
| PeggingTrace Factory           | 按 `DemandFactoryCode`                                  |
| Explanation Scope              | 由2号位Query契约按运行对象映射并过滤                                  |
| PI Position Factory            | 按PI所属生产工厂，授权后查看该PI完整Position                           |
| BusinessFactIssue NULL Factory | 不自动放行；能映射则按其它Scope，真正全局Issue仅Global用户                  |
| G8 DomainStatus                | 必须Domain Scope；正式路径5→2 Query                           |
| Demand Protection Release      | 5号位只鉴权/Scope/中转，2号位Application Service实际释放             |
| 11个Controller统一PlanView        | 只能临时堵漏，正式应按业务动作使用对应Permission                          |

最后还有一个治理动作需要明确：**这次Q1不是要修改冻结文档增加DepartmentCode，而是要修改/撤回9月12日执行清单中的错误要求。** 最新v5.1.6字段说明和DDL本身是正确的，不应为了追赶前端DTO而升版增加一个没有业务来源的字段。

如果你要直接发给5号位，我建议就把上面“Q1～Q6正式裁定 + 最终执行结论”作为正式回复，不再让他们自行二次解释。
