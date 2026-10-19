# -*- coding: utf-8 -*-
"""產生外掛使用的三個資料檔：
   CodeCheck_Laws.json     官方條文（全國法規資料庫開放資料 + 無障礙規範 108 年版）
   CodeCheck_Rules.json    可執行規則
   CodeCheck_Catalog.json  每一條條文的檢討方式（自動／半自動／人工／不適用模型）與對應規則
"""
import json, re, sys, collections
sys.path.insert(0, '.')
from rules_tw import R

lib = json.load(open('lib/law_library.json', encoding='utf-8'))
acc = json.load(open('pdf/acc_clauses.json', encoding='utf-8'))

# ── 無障礙規範條文整理：把被 PDF 拆開的圖號片段併回前一條
fixed = []
for c in acc:
    if fixed and (re.match(r'^[﹚）)，,。、；]', c['text']) or c['text'] == ''):
        fixed[-1]['text'] += c['no'] + c['text']
        continue
    fixed.append(dict(c))
acc = fixed

LAW_ORDER = ['D0070114', 'D0070115', 'ACC', 'D0070117', 'D0070116', 'D0120029', 'D0070109', 'D0070141', 'D0070208', 'J0110110', 'D0070001']
laws_out = []
for pc in LAW_ORDER:
    if pc == 'ACC':
        laws_out.append({
            'code': 'ACC', 'name': '建築物無障礙設施設計規範', 'level': '行政規則',
            'modified': '20190104', 'effective': '20190701',
            'source': '內政部主管法規共用系統 glrs.moi.gov.tw GL000734（108 年 7 月 1 日生效版）',
            'articles': [{'no': c['no'], 'chapter': c['chapter'], 'text': c['text']} for c in acc]})
        continue
    law = next(l for l in lib['laws'] if l['pcode'] == pc)
    arts = [a for a in law['articles'] if not re.fullmatch(r'（?刪除）?。?', a['text'].strip())]
    laws_out.append({
        'code': pc, 'name': law['name'], 'level': law['level'], 'modified': law['modified'],
        'effective': law['effective'], 'source': '全國法規資料庫 law.moj.gov.tw 開放資料（2026-10-17 下載）',
        'articles': [{'no': a['no'].replace('第', '').replace('條', ''), 'chapter': a['chapter'], 'text': a['text']} for a in arts]})

# ── 規則
ids = [r['id'] for r in R]
dups = [k for k, v in collections.Counter(ids).items() if v > 1]
assert not dups, dups
rules_by_art = collections.defaultdict(list)
for r in R:
    rules_by_art[(r['law'], r['art'])].append(r)

# ── 分類
def classify(law, art):
    code, no, ch, text = law['code'], art['no'], art['chapter'], art['text']
    rs = rules_by_art.get((code, no), [])
    if rs:
        kinds = {r['kind'] for r in rs}
        return ('auto' if kinds == {'auto'} else 'semi'), '已有可執行規則'
    n = no.split('-')[0].split('.')[0]
    num = int(n) if n.isdigit() else 0
    t = text
    if code == 'D0070114':
        return 'info', '總則：適用範圍、名詞與程序'
    if code == 'D0070109':
        return 'info', '建築法：行政程序、執照、罰則（非模型檢討）'
    if code == 'D0070001':
        return 'info', '都市計畫法：計畫程序（土地使用管制請於專案設定填入）'
    if code == 'D0070141':
        return 'info', '使用類組定義與變更使用程序（類組設定見專案資訊）'
    if code == 'D0070116':
        return 'info', '構造編：結構設計與計算，應以結構計算書／結構審查檢討'
    if code == 'D0070208' or code == 'J0110110':
        return 'manual', '依設計技術規範另行計算'
    if code == 'D0070117':
        if any(k in t for k in ['試驗', '絕緣電阻', '加壓', 'CNS', '國家標準', '材料']):
            return 'manual', '設備規格、材料與竣工試驗（文件審查）'
        return 'manual', '設備設計（設備圖說與計算書）'
    if code == 'ACC':
        if no.startswith('A'):
            return 'info', '參考附錄（非強制）'
        if num < 200 or re.search(r'適用範圍|依據|用語定義', t[:12]):
            return 'info', '總則／適用範圍'
        return 'manual', '細部設施（五金、標誌、扶手形式、求助鈴等）—以族群細部或現場檢查'
    if code == 'D0120029':
        if num >= 193:
            return 'manual', '公共危險物品場所（另依危險物品專章檢討）'
        if num <= 13:
            return 'info', '總則：用語、場所分類'
        if any(k in t for k in ['試驗', '絕緣電阻', '水源', '幫浦', '壓力', '配管', '蓄電池', '藥劑', '容器', '配線', 'CNS']):
            return 'manual', '系統設計／水力計算／竣工試驗（消防圖說與計算書）'
        return 'manual', '消防設備細部（依消防圖說檢討）'
    if code == 'D0070115':
        if num == 1 or '用語' in ch and num <= 1:
            return 'info', '用語定義（供其他條文引用）'
        if 150 <= num <= 159:
            return 'info', '施工安全措施（施工階段現場管理）'
        if 195 <= num <= 200 or 234 <= num <= 239:
            return 'info', '結構設計要求（結構計算書）'
        if 298 <= num <= 323:
            return 'manual', '綠建築指標（依綠建築設計技術規範計算）'
        if 160 <= num <= 166 or 281 <= num <= 292:
            return 'manual', '容積／綜合設計計算（面積計算表）'
        if 260 <= num <= 268:
            return 'manual', '山坡地基地條件（地質、坡度調查）'
        if re.search(r'由(直轄市|中央|內政部|當地)|另定之|定之。$|施行|生效', t) and len(t) < 160:
            return 'info', '授權或程序規定'
        return 'manual', '需人工檢討（模型資訊不足以判定）'
    return 'manual', ''

catalog = []
for law in laws_out:
    for art in law['articles']:
        kind, reason = classify(law, art)
        rs = rules_by_art.get((law['code'], art['no']), [])
        catalog.append({'law': law['code'], 'no': art['no'], 'kind': kind, 'reason': reason,
                        'rules': [r['id'] for r in rs],
                        'hint': re.sub(r'\s+', '', art['text'])[:80]})

# 規則對應不到條文的檢查
known = {(c['law'], c['no']) for c in catalog}
missing = [r['id'] for r in R if (r['law'], r['art']) not in known]
print('rules without article:', missing)

stat = collections.Counter((c['law'], c['kind']) for c in catalog)
for law in laws_out:
    print(law['code'], law['name'], {k: stat[(law['code'], k)] for k in ['auto', 'semi', 'manual', 'info']})
print('total articles', len(catalog), 'rules', len(R))

out = sys.argv[1] if len(sys.argv) > 1 else 'out'
import os
os.makedirs(out, exist_ok=True)
def dump(name, obj):
    with open(os.path.join(out, name), 'w', encoding='utf-8') as f:
        json.dump(obj, f, ensure_ascii=False, indent=None, separators=(',', ':'))
dump('CodeCheck_Laws.json', {'version': '2026.10.17', 'laws': laws_out})
dump('CodeCheck_Rules.json', {'version': '2026.10.17', 'rules': R})
dump('CodeCheck_Catalog.json', {'version': '2026.10.17', 'items': catalog})
