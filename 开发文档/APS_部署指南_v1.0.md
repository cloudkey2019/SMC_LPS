# APS 部署指南

**版本**：v1.0  
**日期**：2026-03-31  
**适用人员**：2号位（技术负责人）、运维人员  
**技术栈**：.NET 8.0 + SQL Server 2019 + Vue 3 + IIS/Kestrel

---

## 📋 部署环境规划

### **三套环境**

| 环境 | 用途 | 服务器 | 数据库 | 部署方式 |
|------|------|--------|--------|---------|
| DEV | 日常开发联调 | 开发机本地 | 本地 SQL Server | 本地运行 |
| UAT | 用户验收测试 | 内网测试服务器 | 测试 SQL Server | 手动部署 |
| PROD | 生产环境 | 生产服务器 | 生产 SQL Server | 审批后部署 |

### **服务器资源要求**

| 资源 | DEV | UAT | PROD |
|------|-----|-----|------|
| CPU | 4核+ | 8核+ | 16核+ |
| 内存 | 16GB+ | 64GB+ | 256GB |
| SSD | 256GB | 512GB | 1TB |
| HDD（快照存储） | - | 1TB | 4.76TB |
| 操作系统 | Windows 10/11 | Windows Server 2019+ | Windows Server 2019+ |

### **数据库规划**

| 数据库 | 用途 | 部署位置 |
|--------|------|---------|
| APS_Production | 业务主库 | SSD（高频I/O） |
| APS_Auth | 权限库（独立） | SSD |
| MES_Integration | ODS库（MES服务器） | SSD（MES服务器上） |

---

## 🔧 DEV 环境部署（开发机本地）

### **步骤1：安装基础环境**

```powershell
# 安装 .NET 8.0 SDK
winget install Microsoft.DotNet.SDK.8

# 安装 SQL Server 2019 Developer Edition
# 下载地址：https://www.microsoft.com/sql-server/sql-server-downloads
# 安装时选择 Developer 版本（免费）

# 安装 SSMS
# 下载地址：https://aka.ms/ssmsfullsetup

# 安装 Node.js 20 LTS（4号位）
winget install OpenJS.NodeJS.LTS
npm install -g pnpm

# 验证
dotnet --version    # 8.0.x
node --version      # v20.x.x（4号位）
```

### **步骤2：创建数据库**

```sql
-- 1. 创建 APS_Production 数据库
CREATE DATABASE APS_Production
ON PRIMARY (
    NAME = 'APS_Production',
    FILENAME = 'D:\SQLData\APS_Production.mdf',
    SIZE = 1GB,
    FILEGROWTH = 256MB
)
LOG ON (
    NAME = 'APS_Production_Log',
    FILENAME = 'D:\SQLData\APS_Production_Log.ldf',
    SIZE = 256MB,
    FILEGROWTH = 128MB
);

-- 2. 创建 APS_Auth 数据库
CREATE DATABASE APS_Auth
ON PRIMARY (
    NAME = 'APS_Auth',
    FILENAME = 'D:\SQLData\APS_Auth.mdf',
    SIZE = 256MB,
    FILEGROWTH = 64MB
)
LOG ON (
    NAME = 'APS_Auth_Log',
    FILENAME = 'D:\SQLData\APS_Auth_Log.ldf',
    SIZE = 64MB,
    FILEGROWTH = 32MB
);

-- 3. 执行 DDL 脚本
-- 在 SSMS 中打开 APS_数据库表结构设计_v2.0.sql，切换到 APS_Production 库执行
-- 在 SSMS 中打开 APS_Auth数据库DDL_v1.0.sql，切换到 APS_Auth 库执行
```

### **步骤3：配置应用**

```json
// appsettings.Development.json
{
  "ConnectionStrings": {
    "ApsProduction": "Server=localhost;Database=APS_Production;Trusted_Connection=True;TrustServerCertificate=True;",
    "ApsAuth": "Server=localhost;Database=APS_Auth;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Jwt": {
    "Secret": "DEV-ONLY-SECRET-KEY-DO-NOT-USE-IN-PRODUCTION-1234567890",
    "Issuer": "APS-Dev",
    "Audience": "APS-Dev",
    "ExpirationHours": 2
  },
  "Hangfire": {
    "ConnectionString": "Server=localhost;Database=APS_Production;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Snapshot": {
    "StoragePath": "D:\\APS_Snapshots",
    "CompressionLevel": "Optimal"
  }
}
```

### **步骤4：启动应用**

```powershell
# 后端
cd src\APS.WebApi
dotnet run

# 前端（4号位）
cd src\APS.WebUI
pnpm install
pnpm dev
```

---

## 🖥️ UAT 环境部署

### **步骤1：服务器准备**

```powershell
# 安装 .NET 8.0 Runtime（不需要 SDK）
winget install Microsoft.DotNet.Runtime.8

# 安装 ASP.NET Core Runtime
winget install Microsoft.DotNet.AspNetCore.8

# 安装 IIS（如果使用 IIS 托管）
Install-WindowsFeature -Name Web-Server -IncludeManagementTools
Install-WindowsFeature -Name Web-Asp-Net45

# 安装 ASP.NET Core Module
# 下载：https://dotnet.microsoft.com/download/dotnet/8.0 → Hosting Bundle
```

### **步骤2：发布应用**

```powershell
# 在开发机上发布
cd src\APS.WebApi
dotnet publish -c Release -o .\publish

# 前端构建
cd src\APS.WebUI
pnpm build
# 构建产物在 dist/ 目录

# 将 publish/ 和 dist/ 复制到 UAT 服务器
```

### **步骤3：配置 IIS**

1. 打开 IIS Manager
2. 创建应用池：`APS-AppPool`（.NET CLR 版本 = 无托管代码）
3. 创建网站：`APS-WebApi`
   - 物理路径：`D:\Apps\APS\api`
   - 端口：5000
4. 创建网站：`APS-WebUI`
   - 物理路径：`D:\Apps\APS\web`
   - 端口：80

### **步骤4：配置数据库**

```sql
-- 在 UAT 数据库服务器上创建数据库（同 DEV 步骤，路径改为服务器路径）
-- 执行 DDL 脚本
-- 导入测试数据
```

### **步骤5：验证**

```powershell
# 检查后端 API
Invoke-RestMethod -Uri "http://uat-server:5000/api/v1/health"

# 检查前端
# 浏览器打开 http://uat-server/
```

---

## 🏭 PROD 环境部署

### **前置条件**

- [ ] 所有测试通过（单元+集成+E2E）
- [ ] UAT 验收通过
- [ ] 部署审批已通过
- [ ] 数据库备份已完成
- [ ] 回滚方案已准备

### **部署步骤**

**⚠️ 生产环境部署必须由2号位执行或监督，其他人禁止操作生产服务器。**

1. **备份现有版本**
```powershell
# 备份应用目录
Copy-Item -Path "D:\Apps\APS" -Destination "D:\Backup\APS_$(Get-Date -Format 'yyyyMMdd_HHmmss')" -Recurse

# 备份数据库
# 在 SSMS 中执行完整备份
```

2. **停止应用**
```powershell
# 停止 IIS 应用池
Stop-WebAppPool -Name "APS-AppPool"
```

3. **部署新版本**
```powershell
# 复制新版本文件
Copy-Item -Path "\\build-server\publish\*" -Destination "D:\Apps\APS\api" -Recurse -Force
Copy-Item -Path "\\build-server\dist\*" -Destination "D:\Apps\APS\web" -Recurse -Force
```

4. **执行数据库迁移**（参考《APS_数据库迁移指南_v1.0.md》）

5. **启动应用**
```powershell
Start-WebAppPool -Name "APS-AppPool"
```

6. **验证**
```powershell
# 健康检查
Invoke-RestMethod -Uri "http://localhost:5000/api/v1/health"

# 核心功能验证
# - 登录是否正常
# - 排程是否能正常发起
# - 数据是否正确
```

7. **监控（部署后30分钟）**
- 检查应用日志：无异常错误
- 检查数据库连接池：连接数正常
- 检查内存使用：无异常增长
- 检查 Hangfire Dashboard：作业正常

### **回滚方案**

```powershell
# 如果部署后发现严重问题：

# 1. 停止应用
Stop-WebAppPool -Name "APS-AppPool"

# 2. 恢复备份
Copy-Item -Path "D:\Backup\APS_最新备份\*" -Destination "D:\Apps\APS" -Recurse -Force

# 3. 恢复数据库（如果执行了迁移）
# 在 SSMS 中恢复数据库备份

# 4. 启动应用
Start-WebAppPool -Name "APS-AppPool"

# 5. 验证回滚成功
Invoke-RestMethod -Uri "http://localhost:5000/api/v1/health"
```

---

## 📊 部署检查清单

### **DEV 环境**
- [ ] .NET 8.0 SDK 已安装
- [ ] SQL Server 2019 已安装
- [ ] 数据库已创建（APS_Production + APS_Auth）
- [ ] DDL 脚本已执行
- [ ] appsettings.Development.json 已配置
- [ ] 应用能正常启动
- [ ] 能正常访问 API

### **UAT 环境**
- [ ] .NET 8.0 Runtime 已安装
- [ ] IIS + Hosting Bundle 已安装
- [ ] 数据库已创建并初始化
- [ ] IIS 站点已配置
- [ ] 应用已部署并能正常访问
- [ ] 测试数据已导入

### **PROD 环境**
- [ ] 部署审批已通过
- [ ] 数据库备份已完成
- [ ] 应用目录备份已完成
- [ ] 回滚方案已准备
- [ ] 新版本已部署
- [ ] 数据库迁移已执行
- [ ] 健康检查通过
- [ ] 核心功能验证通过
- [ ] 监控指标正常

---

**维护责任人**：2号位  
**最后更新**：2026-03-31
