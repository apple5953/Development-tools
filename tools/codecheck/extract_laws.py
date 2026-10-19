# -*- coding: utf-8 -*-
"""從官方資料重建法規庫（法規修正時執行）。

需要的檔案（放在本資料夾 raw\\ 下）：
  ChLaw.json.zip、ChOrder.json.zip    全國法規資料庫開放資料 https://law.moj.gov.tw/api/Ch/Law/JSON 、/api/Ch/Order/JSON
  accessible.pdf                     建築物無障礙設施設計規範（內政部主管法規共用系統 GL000734 附件）

輸出：lib/law_library.json、pdf/acc_clauses.json，之後執行 build_codecheck.py 產生外掛資料檔。
需要 pdftotext（poppler）。
"""
import json, re, zipfile, subprocess, os

WANT = {'D0070114', 'D0070115', 'D0070116', 'D0070117', 'D0070109', 'D0120029', 'D0070141', 'D0070001', 'D0070208', 'J0110110'}
os.makedirs('lib', exist_ok=True); os.makedirs('pdf', exist_ok=True)
out = []
for z in ['raw/ChLaw.json.zip', 'raw/ChOrder.json.zip']:
    with zipfile.ZipFile(z) as zf:
        name = [n for n in zf.namelist() if n.endswith('.json')][0]
        data = json.loads(zf.read(name).decode('utf-8-sig'))
    for l in data['Laws']:
        pc = l['LawURL'].split('pcode=')[-1]
        if pc not in WANT:
            continue
        arts, chap = [], ''
        for a in l['LawArticles']:
            if a['ArticleType'] == 'C':
                chap = a['ArticleContent'].strip(); continue
            arts.append({'no': a['ArticleNo'].replace(' ', ''), 'chapter': chap, 'text': a['ArticleContent']})
        out.append({'pcode': pc, 'name': l['LawName'], 'level': l['LawLevel'], 'modified': l['LawModifiedDate'],
                    'effective': l['LawEffectiveDate'], 'effectiveNote': l['LawEffectiveNote'],
                    'attachments': [{'name': x.get('FileName'), 'url': x.get('FileURL')} for x in (l.get('LawAttachements') or [])],
                    'articles': arts})
json.dump({'laws': out}, open('lib/law_library.json', 'w', encoding='utf-8'), ensure_ascii=False)

subprocess.run(['pdftotext', 'raw/accessible.pdf', 'pdf/acc_raw.txt'], check=True)
t = open('pdf/acc_raw.txt', encoding='utf-8').read()
t = re.sub(r'[ \t]+', ' ', t); t = re.sub(r'\n\s*\n+', '\n', t)
lines = [l for l in t.split('\n') if not re.fullmatch(r'\s*[\d\.\-cm ]{0,12}\s*', l)]
idx = [i for i, l in enumerate(lines) if l.strip() == '第一章 總則'][1]
clauses, cur, chap = [], None, ''
for l in lines[idx:]:
    s = l.strip()
    if re.match(r'^第[一二三四五六七八九十]+章', s): chap = s; continue
    if re.match(r'^圖\s*[A\d]', s) or re.match(r'^表\s*\d', s): continue
    m = re.match(r'^((?:A)?\d{3,4}(?:\.\d+)*)\s*(.*)$', s)
    if m and (cur is None or m.group(1) != cur['no']):
        cur = {'no': m.group(1), 'chapter': chap, 'text': m.group(2)}; clauses.append(cur)
    elif cur:
        cur['text'] += s
json.dump(clauses, open('pdf/acc_clauses.json', 'w', encoding='utf-8'), ensure_ascii=False)
print('laws', len(out), 'accessibility clauses', len(clauses))
