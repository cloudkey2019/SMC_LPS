import dayjs from 'dayjs'
import utc from 'dayjs/plugin/utc'

dayjs.extend(utc)

/** 后端 DateTime 字段序列化为 naive UTC（System.Text.Json 默认不带 Z 后缀），
 *  前端必须显式按 UTC 解析再转本地，否则少 8h（北京时间）。Dayjs 裸解析按本地时区故出错。 */
export function formatUtcDateTime(s: string | null | undefined): string {
  return s ? dayjs.utc(s).local().format('YYYY-MM-DD HH:mm') : '—'
}

export function formatUtcDateTimeSec(s: string | null | undefined): string {
  return s ? dayjs.utc(s).local().format('YYYY-MM-DD HH:mm:ss') : '—'
}

export function formatUtcMonthDay(s: string | null | undefined): string {
  return s ? dayjs.utc(s).local().format('MM-DD') : '—'
}

export function formatUtcMonthDayHM(s: string | null | undefined): string {
  return s ? dayjs.utc(s).local().format('MM-DD HH:mm') : '—'
}
