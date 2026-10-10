#!/bin/bash
set -e
TOKEN=$(curl -sk -X POST https://localhost:7044/api/auth/login -H "Content-Type: application/json" -d '{"userCode":"admin","password":"Admin@123456"}' | node -e "let d='';process.stdin.on('data',c=>d+=c).on('end',()=>console.log(JSON.parse(d).data.accessToken))")
echo "TOKEN: ${TOKEN:0:20}..."

echo ""
echo "=== Test 1: 全字段（camelCase）==="
RESP=$(curl -sk -X POST 'https://localhost:7044/api/demand-protection/release' \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"lockIds":[2,3],"releasedBy":"verify","releaseReason":"A方案 verify"}')
echo "$RESP" | node -e "let d='';process.stdin.on('data',c=>d+=c).on('end',()=>{try{const j=JSON.parse(d);console.log('code:',j.code,'message:',j.message);if(Array.isArray(j.data))j.data.forEach((x,i)=>console.log('  ['+i+'] lockId='+x.lockId+' status='+x.status))}catch(e){console.log('RAW:',d.slice(0,400))}})"

echo ""
echo "=== Test 2: 全字段（PascalCase）==="
RESP=$(curl -sk -X POST 'https://localhost:7044/api/demand-protection/release' \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"LockIds":[2,3],"ReleasedBy":"verify","ReleaseReason":"A方案 verify"}')
echo "$RESP" | node -e "let d='';process.stdin.on('data',c=>d+=c).on('end',()=>{try{const j=JSON.parse(d);console.log('code:',j.code,'message:',j.message);if(Array.isArray(j.data))j.data.forEach((x,i)=>console.log('  ['+i+'] lockId='+x.lockId+' status='+x.status))}catch(e){console.log('RAW:',d.slice(0,400))}})"

echo ""
echo "=== Test 3: 单字段 lockIds=[2,3] ==="
RESP=$(curl -sk -X POST 'https://localhost:7044/api/demand-protection/release' \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"lockIds":[2,3]}')
echo "$RESP" | node -e "let d='';process.stdin.on('data',c=>d+=c).on('end',()=>{try{const j=JSON.parse(d);console.log('code:',j.code,'message:',j.message);if(Array.isArray(j.data))j.data.forEach((x,i)=>console.log('  ['+i+'] lockId='+x.lockId+' status='+x.status))}catch(e){console.log('RAW:',d.slice(0,400))}})"