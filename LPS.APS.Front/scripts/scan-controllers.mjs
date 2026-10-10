#!/usr/bin/env node
/**
 * scan-controllers.mjs — 4 号位 RBAC 红线扫描
 *
 * 扫 `lps/LPS.APS.Web/Controllers/*.cs`，对每个 `XxxController : ControllerBase`：
 *   - 解析类级 [Authorize] / [AllowAnonymous]
 *   - 解析类内每个 [HttpGet/Post/Put/Delete/Patch] 方法的特性块
 *   - 判定每个公开端点是否「已授权」
 *
 * 状态：
 *   class-auth    — 类级 [Authorize]，方法自动继承（除 [AllowAnonymous]）
 *   method-auth   — 类级无，但每个 Http 方法都有 [Authorize]
 *   partial-auth  — 部分方法缺 [Authorize]（🔴 红）
 *   no-auth       — 全裸（🔴 红）
 *
 * 红线来源：
 *   - 5 号位契约 §一.① P0（ManualEta 写端鉴权）— 已闭环
 *   - 5 号位契约 §一.② P0（11 个查询 Controller 类级 [Authorize]）— 已闭环
 *   - 但 GovernanceController / 部分 Controller 的「类级无 + GET 裸」组合可能漏，
 *     本脚本固化证据，下游收紧提供名单。
 *
 * 用法：
 *   node frontNew/scripts/scan-controllers.mjs                      # 默认扫 lps/LPS.APS.Web/Controllers
 *   node frontNew/scripts/scan-controllers.mjs --dir <path>         # 自定义目录
 *   node frontNew/scripts/scan-controllers.mjs --json               # 仅输出 JSON
 *   node frontNew/scripts/scan-controllers.mjs --strict             # 退出码 = 1 当存在 red
 *
 * 输出：stdout（Markdown 表格 + 末尾 JSON 摘要），退出码 0/1
 */

import { readFileSync, readdirSync } from 'node:fs'
import { resolve, relative } from 'node:path'

/* ====================== CLI ====================== */
const args = parseArgs(process.argv.slice(2))
const dir = resolve(
  process.cwd(),
  args.dir ?? '../../lps/LPS.APS.Web/Controllers'
)
const jsonOnly = args.json === true
const strict = args.strict === true

/* ====================== 扫描 ====================== */
const files = readdirSync(dir)
  .filter((f) => f.endsWith('.cs'))
  .sort()

const results = []
for (const file of files) {
  const path = resolve(dir, file)
  const src = readFileSync(path, 'utf8')
  results.push(scanFile(path, src))
}

/* ====================== 输出 ====================== */
if (!jsonOnly) {
  printMarkdown(results, dir)
}

printJson(results)

if (strict && results.some((r) => r.status === 'partial-auth' || r.status === 'no-auth')) {
  process.exit(1)
}

/* ====================== 实现 ====================== */

function parseArgs(argv) {
  const out = {}
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i]
    if (a === '--dir') {
      out.dir = argv[++i]
    } else if (a === '--json') {
      out.json = true
    } else if (a === '--strict') {
      out.strict = true
    } else if (a === '--help' || a === '-h') {
      printHelp()
      process.exit(0)
    } else {
      console.error(`未知参数：${a}`)
      process.exit(2)
    }
  }
  return out
}

function printHelp() {
  console.log(
    `用法：node scan-controllers.mjs [--dir <path>] [--json] [--strict]\n` +
      `  --dir    Controllers 目录（默认 ../../lps/LPS.APS.Web/Controllers）\n` +
      `  --json   仅输出 JSON\n` +
      `  --strict 当存在 red（partial-auth / no-auth）退出码 = 1`
  )
}

/**
 * 扫单个 .cs 文件，返回 { file, className, route, classAuth, methods[] }
 * methods[] = { verb, route, path, auth, allowAnon, line }
 */
function scanFile(filePath, src) {
  const lines = src.split(/\r?\n/)

  // 1. 找类声明 `class XxxController : ControllerBase`
  let classDeclLine = -1
  let className = null
  for (let i = 0; i < lines.length; i++) {
    const m = lines[i].match(
      /\bclass\s+([A-Za-z_][A-Za-z0-9_]*Controller)\s*:\s*ControllerBase\b/
    )
    if (m) {
      classDeclLine = i
      className = m[1]
      break
    }
  }

  if (classDeclLine === -1) {
    return null // 非 Controller 文件，跳过
  }

  // 2. 类级 [Route(...)] / [ApiController] / [Authorize] / [AllowAnonymous]
  const classRoute = findClassRoute(lines, classDeclLine)
  const classAuth = findAttrAboveClassDecl(lines, classDeclLine, 'Authorize')
  const classAllowAnon = findAttrAboveClassDecl(
    lines,
    classDeclLine,
    'AllowAnonymous'
  )

  // 3. 类内所有 [HttpGet/Post/Put/Delete/Patch] 标记的方法
  const methods = []
  // 找从 classDeclLine 到类结束（匹配的 `}` 在顶层行）
  let braceDepth = 0
  let started = false
  let classEndLine = lines.length
  for (let i = classDeclLine; i < lines.length; i++) {
    const line = lines[i]
    for (const ch of line) {
      if (ch === '{') {
        braceDepth++
        started = true
      } else if (ch === '}') {
        braceDepth--
        if (started && braceDepth === 0) {
          classEndLine = i
          break
        }
      }
    }
    if (started && braceDepth === 0) break
  }

  // 找所有 [HttpVerb("...")] 标记的方法签名
  const verbRe = /^\s*\[(HttpGet|HttpPost|HttpPut|HttpDelete|HttpPatch)(?:\(([^)]*)\))?\]\s*$/
  const methodDeclRe = /^\s*public\s+(?:async\s+)?[A-Za-z_][\w<>?,\s\[\]]*?\s+([A-Za-z_]\w*)\s*\(/

  for (let i = classDeclLine + 1; i < classEndLine; i++) {
    const m = lines[i].match(verbRe)
    if (!m) continue
    const verb = m[1]
    const route = m[2] ?? ''
    // 找方法签名（向下）
    let methodLine = -1
    let methodName = null
    for (let j = i + 1; j < Math.min(i + 6, classEndLine); j++) {
      const mm = lines[j].match(methodDeclRe)
      if (mm) {
        methodLine = j
        methodName = mm[1]
        break
      }
    }
    if (methodLine === -1) continue

    const methodAuth = findAttrNearVerb(lines, i, methodLine, 'Authorize')
    const methodAllowAnon = findAttrNearVerb(
      lines,
      i,
      methodLine,
      'AllowAnonymous'
    )
    methods.push({
      verb,
      route: route.replace(/^["']|["']$/g, ''),
      methodName,
      line: i + 1,
      auth: methodAuth,
      allowAnon: methodAllowAnon
    })
  }

  // 4. 判定 status
  let status
  if (classAuth) {
    status = 'class-auth'
  } else {
    const httpMethods = methods
    const unauthCount = httpMethods.filter((m) => !m.auth && !m.allowAnon).length
    if (unauthCount === 0) status = 'method-auth'
    else if (unauthCount === httpMethods.length) status = 'no-auth'
    else status = 'partial-auth'
  }

  return {
    file: relative(process.cwd(), filePath).replace(/\\/g, '/'),
    className,
    classRoute,
    classAuth,
    classAllowAnon,
    status,
    methods
  }
}

/**
 * 方法级特性扫描：以 verb 行为中心，向下扫到 signature，向上扫到块边界
 * （遇到第一个非 [ 开头非空非注释行）。
 * C# 实际习惯：[Authorize] / [AllowAnonymous] 既可能在 verb 之上（GovernanceController），
 * 也可能在 verb 之下（AuthController Login）。
 */
function findAttrNearVerb(lines, verbIdx, sigIdx, name) {
  let upper = verbIdx
  for (let i = verbIdx - 1; i >= Math.max(0, verbIdx - 12); i--) {
    const line = lines[i].trim()
    if (line === '' || line.startsWith('//')) continue
    if (!line.startsWith('[')) break
    upper = i
  }
  for (let i = upper; i < sigIdx; i++) {
    const line = lines[i].trim()
    if (line === '' || line.startsWith('//')) continue
    if (new RegExp(`\\[\\s*${name}\\b`).test(line)) return true
  }
  return false
}

/**
 * 类级 [Authorize]：类声明上方连续 [ 开头的特性块内查找。
 * 类级特性块 = 一个或多个紧邻的 `[` 行；遇到第一个不以 `[` 开头的非空非注释行即停止。
 */
function findAttrAboveClassDecl(lines, declLine, name) {
  for (let i = declLine - 1; i >= 0; i--) {
    const line = lines[i].trim()
    if (line === '' || line.startsWith('//')) continue
    if (!line.startsWith('[')) return false // 退出类级特性块
    if (new RegExp(`\\[\\s*${name}\\b`).test(line)) return true
  }
  return false
}

/**
 * 类级 [Route("...")] 提取；支持 "api/[controller]" 这种字面量替换为类名。
 */
function findClassRoute(lines, declLine) {
  const upper = Math.max(0, declLine - 8)
  for (let i = declLine - 1; i >= upper; i--) {
    const m = lines[i].match(/\[Route\(\s*"([^"]+)"\s*\)\]/)
    if (m) return m[1]
  }
  return null
}

/* ====================== Markdown 输出 ====================== */

function printMarkdown(results, dir) {
  const r = results.filter(Boolean)
  console.log(`# Controllers Auth Coverage Scan`)
  console.log()
  console.log(`目录：\`${relative(process.cwd(), dir).replace(/\\/g, '/')}\``)
  console.log(`文件数：${r.length}`)
  console.log()
  console.log(
    `| # | Controller | 状态 | 类级 Auth | 路由前缀 | Http 方法 | 未授权端点 |`
  )
  console.log(
    `|---|---|---|---|---|---|---|`
  )
  let idx = 1
  for (const c of r) {
    const route = c.classRoute
      ? c.classRoute.replace('[controller]', c.className.replace('Controller', '').toLowerCase())
      : '—'
    const httpMethods = c.methods
      .filter((m) => m.verb && m.methodName)
      .map((m) => `${m.verb}${m.route ? ` ${m.route}` : ''}`)
    const unauth = c.methods
      .filter((m) => !m.auth && !m.allowAnon)
      .map((m) => `${m.verb} ${m.methodName}`)
    const statusEmoji =
      c.status === 'class-auth' || c.status === 'method-auth'
        ? '✅'
        : c.status === 'partial-auth'
          ? '🟡'
          : '🔴'
    console.log(
      `| ${idx++} | \`${c.className}\` | ${statusEmoji} ${c.status} | ${
        c.classAuth ? '✅' : '—'
      } | \`${route}\` | ${httpMethods.length} | ${unauth.length === 0 ? '—' : unauth.join(', ')} |`
    )
  }
  console.log()
  console.log(`## 红名单（partial-auth / no-auth）`)
  console.log()
  const red = r.filter(
    (c) => c.status === 'partial-auth' || c.status === 'no-auth'
  )
  if (red.length === 0) {
    console.log(`✅ 无。`)
  } else {
    for (const c of red) {
      console.log(
        `### 🔴 \`${c.className}\`（${c.status}）— \`${c.file}\``
      )
      console.log()
      console.log(`| 方法 | 行号 | Verb | Route | Authorize | AllowAnonymous |`)
      console.log(`|---|---|---|---|---|---|`)
      for (const m of c.methods) {
        if (m.auth || m.allowAnon) continue // 只列未授权
        console.log(
          `| \`${m.methodName}\` | ${m.line} | ${m.verb} | \`${m.route || ''}\` | ${
            m.auth ? '✅' : '❌'
          } | ${m.allowAnon ? '✅' : '—'} |`
        )
      }
      console.log()
    }
  }
  console.log(`## 绿名单（method-auth — 类级无 Authorize 但每个 Http 方法都有）`)
  console.log()
  const green = r.filter((c) => c.status === 'method-auth')
  if (green.length === 0) {
    console.log(`（无）`)
  } else {
    for (const c of green) {
      console.log(
        `- ✅ \`${c.className}\`：${c.methods.length} 个 Http 方法全部有 [Authorize]`
      )
    }
  }
  console.log()
  console.log(`## 类级 [Authorize] 控制器（class-auth）`)
  console.log()
  const classAuth = r.filter((c) => c.status === 'class-auth')
  if (classAuth.length === 0) {
    console.log(`（无）`)
  } else {
    for (const c of classAuth) {
      console.log(`- ✅ \`${c.className}\`：${c.methods.length} 个端点`)
    }
  }
  console.log()
}

function printJson(results) {
  const summary = {
    scannedAt: new Date().toISOString(),
    totalFiles: results.filter(Boolean).length,
    classAuth: results.filter((r) => r?.status === 'class-auth').length,
    methodAuth: results.filter((r) => r?.status === 'method-auth').length,
    partialAuth: results.filter((r) => r?.status === 'partial-auth').length,
    noAuth: results.filter((r) => r?.status === 'no-auth').length,
    redList: results
      .filter((r) => r?.status === 'partial-auth' || r?.status === 'no-auth')
      .map((r) => ({
        file: r.file,
        className: r.className,
        status: r.status,
        unauthEndpoints: r.methods
          .filter((m) => !m.auth && !m.allowAnon)
          .map((m) => `${m.verb} ${m.route || '/'}`)
      }))
  }
  console.log(JSON.stringify(summary, null, 2))
}