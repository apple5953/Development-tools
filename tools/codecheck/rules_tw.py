# -*- coding: utf-8 -*-
"""
可執行的法規規則（台灣）。每條規則對應官方條文，門檻逐字對照 law_library.json / 無障礙規範 108 年版。

欄位：
  id       規則代碼
  law      法規代碼（全國法規資料庫 pcode；ACC = 建築物無障礙設施設計規範）
  art      條號（與法規庫一致，如 "33"、"167-3"、"204.2.2"）
  title    規則名稱
  kind     auto（模型可直接量）/ semi（需專案輸入或人工確認的計算）
  scope    project / level / room / stair / door / window / railing / ramp / parking / device
  where    篩選元素的條件（元素變數）
  applies  適用條件（專案變數；可用元素變數）
  measure  受檢數值（運算式）
  op       >= <= > < == !=
  limit    門檻（運算式）或 cases：[[條件, 門檻], ...] 取第一個成立者
  unit     單位（顯示用）
  msg      說明（顯示在結果）
運算式語法見 C# Expr.cs：+ - * / 比較 && || ! ， has('A-1','B') 判斷專案使用類組，
in(x,'a','b') ，if(c,a,b)，min/max/ceil/floor。
"""

R = []

def rule(**kw):
    kw.setdefault('kind', 'auto')
    kw.setdefault('severity', 'error')
    R.append(kw)

TC = 'D0070115'   # 建築技術規則建築設計施工編
EQ = 'D0070117'   # 建築設備編
FS = 'D0120029'   # 各類場所消防安全設備設置標準
ACC = 'ACC'       # 建築物無障礙設施設計規範（108.07.01 施行）

# ───────────────────────── 第 1 條 用語（高度、層數、屋突） ─────────────────────────
rule(id='TC-001-9a', law=TC, art='1', title='屋頂突出物高度（一般）', scope='level',
     where='isRoofProjection', applies='true',
     measure='projectionHeightMm', op='<=', limit='if(hasElevatorToRoof, 9000, 6000)', unit='mm',
     msg='屋頂突出物高度 6 m 以內（有昇降機通達屋頂者 9 m），超過即計入建築物高度與層數。')
rule(id='TC-001-9b', law=TC, art='1', title='屋頂突出物水平投影面積比', scope='project', kind='semi',
     applies='buildingAreaM2 > 0',
     measure='roofProjectionAreaM2', op='<=',
     limit='max(buildingAreaM2 * if(isHighRise, 0.15, 0.125), 25)', unit='m²',
     msg='屋突水平投影面積和 ≤ 建築面積 12.5%（高層 15%），未達 25 m² 得建 25 m²。建築面積取自專案設定。')
rule(id='TC-001-9c', law=TC, art='1', title='女兒牆高度', scope='project', kind='semi', severity='warning',
     applies='parapetHeightMm > 0', measure='parapetHeightMm', op='<=', limit='1500', unit='mm',
     msg='女兒牆高度 1.5 m 以內不計入建築物高度。')

# ───────────────────────── 第 14、23、24 條 高度限制（需道路寬度） ─────────────────────────
rule(id='TC-014', law=TC, art='14', title='建築物高度 ≤ 1.5 × 面前道路寬 + 6 m', scope='project', kind='semi',
     applies='!volumeControl && roadWidthM > 0',
     measure='buildingHeightM', op='<=', limit='1.5 * roadWidthM + 6', unit='m',
     msg='未實施容積管制地區適用（第 166 條）。面前道路寬度依專案設定。')
rule(id='TC-023', law=TC, art='23', title='住宅區高度 ≤ 21 m 且 ≤ 7 層', scope='project', kind='semi',
     applies="!volumeControl && zone == '住宅區' && !tc23Exception",
     measure='if(buildingHeightM <= 21 && floorsAbove <= 7, 1, 0)', op='==', limit='1', unit='',
     msg='住宅區建築物高度不得超過 21 m 及 7 層（有第 23 條但書者不在此限）。')
rule(id='TC-024', law=TC, art='24', title='未實施容積管制地區 ≤ 36 m 且 ≤ 12 層', scope='project', kind='semi',
     applies='!volumeControl && !tc24Exception',
     measure='if(buildingHeightM <= 36 && floorsAbove <= 12, 1, 0)', op='==', limit='1', unit='',
     msg='未實施容積管制地區建築物高度不得超過 36 m 及 12 層。')
rule(id='TC-164', law=TC, art='164', title='容積管制地區斜率高度 H ≤ 3.6 (Sw + D)', scope='project', kind='semi',
     applies='volumeControl && roadWidthM > 0',
     measure='buildingHeightM', op='<=', limit='3.6 * (roadWidthM + setbackM)', unit='m',
     msg='簡化檢討：以建築物最高點至建築線水平距離 D（專案設定）計算；陰影面積條件須另行檢討。')
rule(id='TC-268', law=TC, art='268', title='山坡地建築物高度', scope='project', kind='semi',
     applies='slopeland && maxCoverageRatio > 0',
     measure='buildingHeightM', op='<=', limit='maxFarRatio / maxCoverageRatio * 3.6 * 2', unit='m',
     msg='山坡地建築物高度 ≤ 法定最大容積率 ÷ 法定最大建蔽率 × 3.6 × 2。')

# ───────────────────────── 第 27、164-1 條 住宅樓層高度 ─────────────────────────
rule(id='TC-164-1a', law=TC, art='164-1', title='住宅地面一層樓層高度 ≤ 4.2 m', scope='level', kind='auto',
     where='levelIndex == 1', applies="has('H-2') && volumeControl && !hasVoid",
     measure='storeyHeightMm', op='<=', limit='4200', unit='mm',
     msg='住宅、集合住宅未設計挑空者，地面一層樓層高度不得超過 4.2 m。')
rule(id='TC-164-1b', law=TC, art='164-1', title='住宅其餘樓層高度 ≤ 3.6 m', scope='level', kind='auto',
     where='levelIndex >= 2 && !isRoofLevel', applies="has('H-2') && volumeControl && !hasVoid",
     measure='storeyHeightMm', op='<=', limit='3600', unit='mm',
     msg='住宅、集合住宅未設計挑空者，其餘各樓層高度不得超過 3.6 m（同戶不同板高最高 4.2 m）。')

# ───────────────────────── 第 32 條 天花板淨高 ─────────────────────────
rule(id='TC-032-1', law=TC, art='32', title='學校教室天花板淨高 ≥ 3 m', scope='room',
     where="role == 'classroom'", applies='true',
     measure='ceilingAvgMm', op='>=', limit='3000', unit='mm',
     msg='學校教室天花板淨高不得小於 3 m（天花板高低不同時以容積 ÷ 面積計）。')
rule(id='TC-032-2', law=TC, art='32', title='居室及浴廁天花板淨高 ≥ 2.1 m', scope='room',
     where="(isHabitable || role == 'toilet' || role == 'bath') && role != 'classroom'", applies='true',
     measure='ceilingAvgMm', op='>=', limit='2100', unit='mm',
     msg='其他居室及浴廁天花板淨高不得小於 2.1 m。')
rule(id='TC-032-3', law=TC, art='32', title='高低天花板最低處 ≥ 1.7 m', scope='room',
     where="isHabitable || role == 'toilet' || role == 'bath'", applies='ceilingMinMm > 0',
     measure='ceilingMinMm', op='>=', limit='1700', unit='mm',
     msg='高低不同之天花板，最低處不得小於 1.7 m，且一半以上須大於 2.1 m。')
rule(id='TC-032-4', law=TC, art='32', title='高低天花板一半以上 > 2.1 m', scope='room',
     where="isHabitable || role == 'toilet' || role == 'bath'", applies='ceilingMinMm > 0 && ceilingMinMm < 2100',
     measure='ceilingAbove2100Ratio', op='>=', limit='0.5', unit='',
     msg='高低不同之天花板高度至少應有一半以上大於 2.1 m。')

# ───────────────────────── 第 33～36 條 樓梯 ─────────────────────────
STAIR_CASES_W = [
    ["isOutdoor && isSafety", "900"],
    ["isOutdoor", "750"],
    ["stairClass == 1 || stairClass == 2", "1400"],
    ["stairClass == 3", "1200"],
    ["true", "750"],
]
rule(id='TC-033-W', law=TC, art='33', title='樓梯寬度', scope='stair', where='!isServiceOnly', applies='true',
     measure='minRunWidthMm', op='>=', cases=STAIR_CASES_W, unit='mm',
     msg='依第 33 條表：兒童用、學校醫院戲院商場等 1.40 m；每層居室 > 200 m² 者 1.20 m；其他 0.75 m。')
rule(id='TC-033-LW', law=TC, art='33', title='樓梯平臺寬度', scope='stair', where='!isServiceOnly && landingCount > 0', applies='true',
     measure='minLandingWidthMm', op='>=', cases=STAIR_CASES_W, unit='mm',
     msg='樓梯平臺寬度與樓梯寬度同標準。')
rule(id='TC-033-R', law=TC, art='33', title='樓梯級高', scope='stair', where='!isServiceOnly', applies='true',
     measure='riserMm', op='<=', cases=[["stairClass == 1", "160"], ["stairClass == 2", "180"], ["true", "200"]], unit='mm',
     msg='級高：兒童用 16 cm、學校醫院戲院商場等 18 cm、其他 20 cm 以下。')
rule(id='TC-033-T', law=TC, art='33', title='樓梯級深', scope='stair', where='!isServiceOnly', applies='true',
     measure='treadMm', op='>=', cases=[["stairClass == 1 || stairClass == 2", "260"], ["stairClass == 3", "240"], ["true", "210"]], unit='mm',
     msg='級深：兒童用與學校等 26 cm、每層居室 > 200 m² 者 24 cm、其他 21 cm 以上。')
rule(id='TC-034', law=TC, art='34', title='樓梯每段高度（設置平臺）', scope='stair', where='!isServiceOnly', applies='true',
     measure='maxFlightRiseMm', op='<=', cases=[["stairClass <= 2", "3000"], ["true", "4000"]], unit='mm',
     msg='第 33 條表第一、二欄每 3 m 以內、其他每 4 m 以內應設平臺。')
rule(id='TC-034-D', law=TC, art='34', title='平臺深度 ≥ 樓梯寬度', scope='stair', where='!isServiceOnly && landingCount > 0', applies='true',
     measure='minLandingDepthMm', op='>=', limit='minRunWidthMm', unit='mm',
     msg='平臺深度不得小於樓梯寬度。')
rule(id='TC-035', law=TC, art='35', title='樓梯淨空 ≥ 190 cm', scope='stair', where='headroomMinMm > 0', applies='true',
     measure='headroomMinMm', op='>=', limit='1900', unit='mm',
     msg='自梯級面最外緣至天花板、梁底或上層樓梯底之垂直淨空不得小於 190 cm。')
rule(id='TC-036', law=TC, art='36', title='樓梯扶手設置', scope='stair', where='!isServiceOnly && totalRiseMm > 1000', applies='true',
     measure='handrailCount', op='>=', limit='if(stairClass >= 3 && hasWallOneSide, 1, 2)', unit='側',
     msg='樓梯內兩側均應裝設扶手；第 33 條第三、四款有壁體者可設一側；高度 1 m 以下免設。', severity='warning')
rule(id='TC-036-M', law=TC, art='36', title='寬度 ≥ 3 m 樓梯中間扶手', scope='stair', where='minRunWidthMm >= 3000', applies='!(riserMm <= 150 && treadMm >= 300)',
     measure='handrailCount', op='>=', limit='3', unit='道', kind='semi', severity='warning',
     msg='樓梯寬度 3 m 以上者應於中間加裝扶手（級高 ≤ 15 cm 且級深 ≥ 30 cm 者免設）。')

# ───────────────────────── 第 38、39、45 條 欄杆、坡道、窗臺 ─────────────────────────
rule(id='TC-038', law=TC, art='38', title='欄杆扶手高度', scope='railing', where='isGuard', applies='true',
     measure='heightMm', op='>=', limit='if(levelIndex >= 10, 1200, 1100)', unit='mm',
     msg='露臺、陽臺、室外走廊、室外樓梯、平屋頂、室內天井等欄杆扶手高度 ≥ 1.10 m；10 層以上 ≥ 1.20 m。')
rule(id='TC-039', law=TC, art='39', title='代替樓梯之坡道坡度 ≤ 1/8', scope='ramp', where='!isVehicle', applies='true',
     measure='slope', op='<=', limit='1/8', unit='',
     msg='建築物內應設樓梯以坡道代替者，坡度不得超過 1:8。')
rule(id='TC-045-5', law=TC, art='45', title='開啟式窗戶窗臺高度', scope='window', where='isExterior && isOperable',
     applies="has('H-2','D-3','F-3') && !hasGuardOutside",
     measure='sillMm', op='>=', limit='if(levelIndex >= 10, 1200, 1100)', unit='mm',
     msg='H-2、D-3、F-3 組外牆開啟式窗戶窗臺高度 ≥ 1.10 m，10 層以上 ≥ 1.20 m（鄰陽臺或設合格欄杆者除外）。')

# ───────────────────────── 第 41～43 條 採光、通風 ─────────────────────────
rule(id='TC-041-1', law=TC, art='41', title='教室採光面積 ≥ 1/5', scope='room', where="role == 'classroom' || role == 'kindergarten'", applies='true',
     measure='daylightRatio', op='>=', limit='1/5', unit='', kind='semi',
     msg='幼兒園及學校教室採光面積不得小於樓地板面積 1/5（窗地板面以上 75 cm 以下部分不計；有效採光距離另檢討）。')
rule(id='TC-041-2', law=TC, art='41', title='住宅居室等採光面積 ≥ 1/8', scope='room',
     where="role == 'bedroom' || role == 'living' || role == 'ward' || (isHabitable && in(roomUse, 'H-1', 'H-2', 'F-1', 'F-3'))",
     applies='true', measure='daylightRatio', op='>=', limit='1/8', unit='', kind='semi',
     msg='住宅居室、寄宿舍臥室、病房、兒童福利設施居室採光面積不得小於樓地板面積 1/8。')
rule(id='TC-043-1', law=TC, art='43', title='居室及浴廁通風面積 ≥ 5%', scope='room', where="(isHabitable || role == 'toilet' || role == 'bath') && !hasMechanicalVentilation",
     applies='true', measure='ventRatio', op='>=', limit='0.05', unit='', kind='semi',
     msg='一般居室及浴廁可開啟窗戶或開口之有效通風面積 ≥ 樓地板面積 5%（設合格機械通風者免）。開啟比例依專案設定估算。')
rule(id='TC-043-2', law=TC, art='43', title='廚房通風面積 ≥ 1/10 且 ≥ 0.8 m²', scope='room', where="role == 'kitchen' && !hasMechanicalVentilation",
     applies='true', measure='min(ventRatio * 10, ventAreaM2 / 0.8)', op='>=', limit='1', unit='', kind='semi',
     msg='廚房有效通風開口 ≥ 樓地板面積 1/10 且 ≥ 0.8 m²（設合格機械通風者免）。')

# ───────────────────────── 第 47 條 廁所 ─────────────────────────
rule(id='TC-047', law=TC, art='47', title='有居室且 ≥ 30 m² 應設廁所', scope='project', applies='habitableAreaM2 >= 30',
     measure='toiletRoomCount', op='>=', limit='1', unit='間', severity='warning',
     msg='凡有居室之建築物，其樓地板面積達 30 m² 以上者應設置廁所。')

# ───────────────────────── 第 55、106 條 昇降機 ─────────────────────────
rule(id='TC-055', law=TC, art='55', title='六層以上應設昇降機', scope='project', applies='floorsAbove >= 6',
     measure='elevatorCount', op='>=', limit='1', unit='座',
     msg='六層以上之建築物至少應設置一座以上之昇降機通達避難層。')
rule(id='TC-106', law=TC, art='106', title='緊急用昇降機數量', scope='project',
     applies='floorsAbove > 10 && maxFloorAreaAbove10M2 > 0 && sumFloorAreaAbove10M2 >= 500',
     measure='emergencyElevatorCount', op='>=', limit='if(maxFloorAreaAbove10M2 <= 1500, 1, 1 + ceil((maxFloorAreaAbove10M2 - 1500) / 3000))', unit='座',
     msg='高度超過十層樓部分最大一層樓地板面積 ≤ 1,500 m² 設一座，超過每達 3,000 m² 增設一座。緊急用昇降機以名稱含「緊急」辨識。')
rule(id='TC-107-6', law=TC, art='107', title='緊急昇降機間面積 ≥ 10 m²', scope='room', where="role == 'emergencyElevatorLobby'", applies='true',
     measure='areaM2', op='>=', limit='10', unit='m²',
     msg='每座緊急昇降機間之樓地板面積不得小於 10 m²。')

# ───────────────────────── 第 57 條 騎樓 ─────────────────────────
rule(id='TC-057-3', law=TC, art='57', title='騎樓淨高 ≥ 3 m', scope='room', where="role == 'arcade'", applies='true',
     measure='clearMinMm', op='>=', limit='3000', unit='mm', msg='騎樓淨高不得小於 3 m。')
rule(id='TC-057-1', law=TC, art='57', title='騎樓寬度 ≥ 3.5 m', scope='room', where="role == 'arcade'", applies='true', kind='semi',
     measure='minWidthMm', op='>=', limit='3500', unit='mm', severity='warning',
     msg='騎樓寬度自道路境界線至建築物地面層外牆面不得小於 3.5 m（地方另有規定者從其規定）。')

# ───────────────────────── 第 59～62 條 停車 ─────────────────────────
rule(id='TC-059', law=TC, art='59', title='法定停車位數量', scope='project', kind='semi',
     applies='requiredParkingCount > 0', measure='parkingCount', op='>=', limit='requiredParkingCount', unit='輛',
     msg='依第 59 條表（或都市計畫規定）計算之法定停車數量。應設數量由專案設定之各類樓地板面積計算，都市計畫另有規定者從其規定。')
rule(id='TC-060-1', law=TC, art='60', title='停車位尺寸 2.5 × 5.5 m', scope='parking', where='!isMechanical && !isLarge && !isMotorcycle', applies='true',
     measure='min(widthMm / 2500, lengthMm / if(angleDeg > 0 && angleDeg <= 30, 6000, 5500))', op='>=', limit='if(isIndoor, 2300 / 2500, 1)', unit='',
     msg='每輛停車位寬 2.5 m、長 5.5 m（角度 30° 以下長 6 m）。室內 1/5 車位寬度得減 20 cm（不得連續、長邊鄰牆者不得寬減）—此項請另行確認比例。')
rule(id='TC-060-3', law=TC, art='60', title='機械停車位尺寸', scope='parking', where='isMechanical', applies='true', kind='semi',
     measure='min(widthMm / 2200, lengthMm / 5500)', op='>=', limit='1', unit='',
     msg='機械停車位寬 2.5 m（不供乘車人進出部分 2.2 m）、長 5.5 m。')
rule(id='TC-061-1', law=TC, art='61', title='車道寬度', scope='ramp', where='isVehicle', applies='true',
     measure='widthMm', op='>=', limit='if(isTwoWayLane, 5500, 3500)', unit='mm',
     msg='單車道寬 3.5 m 以上，雙車道 5.5 m 以上（服務車位 50 輛以上之雙向通行段應為雙車道）。')
rule(id='TC-061-2', law=TC, art='61', title='車道坡度 ≤ 1/6', scope='ramp', where='isVehicle', applies='true',
     measure='slope', op='<=', limit='1/6', unit='', msg='車道坡度不得超過 1:6。')
rule(id='TC-062-3', law=TC, art='62', title='停車樓層淨高 ≥ 2.1 m', scope='room', where="role == 'parking'", applies='true',
     measure='clearMinMm', op='>=', limit='2100', unit='mm', msg='供停車空間之樓層淨高不得小於 2.1 m（量至梁底或設備下緣）。')

# ───────────────────────── 第 69、70、79、83 條 防火 ─────────────────────────
rule(id='TC-079', law=TC, art='79', title='防火區劃面積 ≤ 1,500 m²', scope='area', where="scheme == 'fire'", applies='fireproof',
     measure='areaM2 * if(sprinklered, 0.5, 1)', op='<=', limit='1500', unit='m²', kind='semi',
     msg='防火構造建築物每 1,500 m² 以一小時防火時效牆壁、防火門窗區劃；有效自動滅火設備範圍得免計 1/2。區劃範圍讀取「防火區劃」面積配置。')
rule(id='TC-083', law=TC, art='83', title='十一層以上防火區劃面積', scope='area', where="scheme == 'fire' && levelIndex >= 11", applies='true',
     measure='areaM2 * if(sprinklered, 0.5, 1)', op='<=',
     cases=[["finishClass == 1 && finishIncludesSubstrate", "500"], ["finishClass == 1", "if(roomUse == 'H-2', 400, 200)"], ["true", "if(roomUse == 'H-2', 200, 100)"]],
     unit='m²', kind='semi',
     msg='十一層以上每 100 m²（H-2 為 200 m²）區劃；1.2 m 以上牆面及天花板耐燃一級者 200 m²（H-2 為 400）；含底材全部耐燃一級者 500 m²；有自動滅火設備得免計 1/2。')
rule(id='TC-076-2', law=TC, art='76', title='防火門門扇尺寸 ≥ 75 × 180 cm', scope='door', where='isFireDoor && !isRollingShutter', applies='true',
     measure='min(leafWidthMm / 750, heightMm / 1800)', op='>=', limit='1', unit='',
     msg='防火門之門扇寬度應在 75 cm 以上，高度應在 180 cm 以上。')
rule(id='TC-076-3', law=TC, art='76', title='常閉防火門單扇面積 ≤ 3 m²', scope='door', where='isFireDoor && !isRollingShutter', applies='true',
     measure='leafAreaM2', op='<=', limit='3', unit='m²', severity='warning',
     msg='常時關閉式防火門單一門扇面積不得超過 3 m²。')

# ───────────────────────── 第 90～99 條 避難 ─────────────────────────
rule(id='TC-090-2', law=TC, art='90', title='直通樓梯於避難層開向屋外出入口 ≥ 1.2 × 1.8 m', scope='door',
     where='levelIndex == 1 && isExterior && servesStair', applies='true',
     measure='min(widthMm / 1200, heightMm / 1800)', op='>=', limit='1', unit='',
     msg='直通樓梯於避難層開向屋外之出入口寬度不得小於 1.2 m，高度不得小於 1.8 m。')
rule(id='TC-090-1', law=TC, art='90', title='避難層二處以上不同方向出入口', scope='project',
     applies="floorsAbove >= 6 || (has('A','B','D','E','F','G','H-1') && publicUseAreaM2 > 500)",
     measure='refugeExitCount', op='>=', limit='2', unit='處', kind='semi',
     msg='六層以上或 A、B、D、E、F、G、H-1 組樓地板面積合計 > 500 m² 者，避難層應開設二處以上不同方向出入口（直接開向道路或避難通路者除外）。')
rule(id='TC-090-1-3', law=TC, art='90-1', title='避難層出入口每處寬高', scope='door', where='levelIndex == 1 && isExterior && isMainEntrance',
     applies="!has('H-2') || has('A-1','B-1','B-2','D-1','D-2')",
     measure="min(widthMm / if(has('A-1','B-1','B-2','D-1','D-2'), 2000, 1200), heightMm / 1800)", op='>=', limit='1', unit='',
     msg='A-1、B-1、B-2、D-1、D-2 組每處出入口寬 ≥ 2 m；其他建築物（住宅除外）每處寬 ≥ 1.2 m；高 ≥ 1.8 m。')
rule(id='TC-091-3', law=TC, art='91', title='避難層以外樓層出入口寬 ≥ 1.2 m', scope='door', where='levelIndex != 1 && servesStair',
     applies="has('A-1','B-1','B-2','D-1','D-2')",
     measure='widthMm', op='>=', limit='1200', unit='mm',
     msg='A-1、B-1、B-2、D-1、D-2 組通達走廊或直通樓梯間之出入口每處寬度不得小於 1.2 m。')
rule(id='TC-092', law=TC, art='92', title='走廊寬度', scope='room', where="role == 'corridor'", applies='art89Applies',
     measure='minWidthMm', op='>=',
     cases=[["in(roomUse, 'D-3', 'D-4', 'D-5') && corridorServesClassroom", "if(isTwoSided, 2400, 1800)"],
            ["roomUse == 'F-1'", "if(isTwoSided, 1600, 1200)"],
            ["levelHabitableAreaM2 >= if(isBasement, 100, 200)", "if(isTwoSided, 1600, 1200)"],
            ["true", "1200"]],
     unit='mm',
     msg='依第 92 條表：教室走廊兩側居室 2.4 m / 其他 1.8 m；F-1 及同層居室 ≥ 200 m²（地下 100 m²）者兩側居室 1.6 m / 其他 1.2 m；其餘 1.2 m。')
rule(id='TC-092-3', law=TC, art='92', title='走廊地板坡度 ≤ 1/10', scope='room', where="role == 'corridor' && floorSlope > 0", applies='art89Applies',
     measure='floorSlope', op='<=', limit='1/10', unit='', severity='warning',
     msg='走廊之地板面有高低時，其坡度不得超過 1/10，並不得設置臺階。')
rule(id='TC-093', law=TC, art='93', title='居室任一點至直通樓梯步行距離', scope='level', where='!isRefugeLevel && hasHabitable', applies='art89Applies',
     measure='maxTravelToStairM', op='<=',
     cases=[["!fireproof", "30"],
            ["has('A','B-1','B-2','B-3','D-1')", "if(levelIndex >= 15, 20, 30)"],
            ["has('C') && !hasLiveAudienceStudio", "70"],
            ["has('C')", "if(levelIndex >= 15, 20, 30)"],
            ["true", "if(levelIndex >= 15, 40, 50)"]],
     unit='m',
     msg='A 類、B-1～B-3、D-1 組 30 m；C 類 70 m；其他 50 m；第 15 層以上 30→20、50→40；非防火構造一律 30 m。以 Revit 行走路徑（PathOfTravel）計算至樓梯第一階或安全梯防火門。')
rule(id='TC-094', law=TC, art='94', title='避難層樓梯口至屋外出入口步行距離', scope='level', where='isRefugeLevel', applies='art89Applies',
     measure='maxTravelStairToExitM', op='<=',
     cases=[["!fireproof", "30"], ["has('A','B-1','B-2','B-3','D-1')", "30"], ["has('C')", "70"], ["true", "50"]],
     unit='m', msg='避難層自樓梯口至屋外出入口之步行距離不得超過第 93 條規定。')
rule(id='TC-095', law=TC, art='95', title='二座以上直通樓梯', scope='level', where='!isRefugeLevel', applies='art89Applies',
     measure='directStairCount', op='>=',
     limit="if(levelIndex >= 8 || (isBasement && floorAreaM2 >= 200) || (has('A-1') && fireproof) || (has('H-1','B-4') && floorAreaM2 > if(fireproof, 240, 100)) || (has('H-2') && isApartment && floorAreaM2 > if(fireproof, 240, 100)) || (levelIndex == 2 && floorAreaM2 > if(fireproof, 400, 200)) || (levelIndex > 2 && floorAreaM2 > if(fireproof, 240, 100)), 2, 1)",
     unit='座',
     msg='八層以上之樓層，或避難層直上層 > 400 m²、其他層 > 240 m²（H-1、B-4、集合住宅 > 240 m²；地下層 ≥ 200 m²；非防火構造面積減半）應設二座以上直通樓梯。')
rule(id='TC-095-2', law=TC, art='95', title='二座樓梯步行路徑重複長度', scope='level', where='!isRefugeLevel && directStairCount >= 2', applies='art89Applies',
     measure='maxSharedPathM', op='<=', limit='allowedTravelM / 2', unit='m', kind='semi',
     msg='居室任一點至二座以上樓梯之步行路徑重複部分不得大於第 93 條最大容許步行距離之 1/2。')
rule(id='TC-096', law=TC, art='96', title='安全梯／特別安全梯', scope='level', where='!isRefugeLevel', applies='true',
     measure='if(requiredStairType == 0, 1, if(requiredStairType == 1, if(safetyStairCount >= 1, 1, 0), if(requiredStairType == 2, if(safetyStairCount >= directStairCount, 1, 0), if(specialOrOutdoorStairCount >= directStairCount, 1, 0))))',
     op='==', limit='1', unit='',
     msg='通達 3～5 層至少一座安全梯；6～14 層或地下二層應設安全梯；15 層以上或地下三層以下應設戶外安全梯或特別安全梯（樓地板 ≤ 100 m² 者得為安全梯）。以樓梯名稱或參數辨識梯別。')
rule(id='TC-097-1', law=TC, art='97', title='安全梯出入口防火門寬度 ≥ 90 cm', scope='door', where='servesSafetyStair', applies='true',
     measure='widthMm', op='>=', limit='900', unit='mm',
     msg='進入安全梯之出入口應裝設具一小時防火時效之防火門，寬度不得小於 90 cm，不得設門檻。')
rule(id='TC-097-1F', law=TC, art='97', title='安全梯出入口防火時效 ≥ 1 小時', scope='door', where='servesSafetyStair', applies='true',
     measure='fireRatingMin', op='>=', limit='60', unit='分', kind='semi',
     msg='進入安全梯之出入口防火門應具一小時以上防火時效及半小時以上阻熱性（讀取門之防火時效參數）。')
rule(id='TC-097-5', law=TC, art='97', title='特別安全梯排煙室面積', scope='level', where='specialStairCount > 0', applies='floorsAbove >= 15 || floorsBelow >= 3',
     measure='smokeLobbyAreaM2', op='>=', limit="levelHabitableAreaM2 * if(has('A-1','B-1','B-2','B-3','D-1','D-2'), 0.05, 0.03)", unit='m²', kind='semi',
     msg='15 層以上或地下三層以下之特別安全梯，樓梯間與排煙室（或陽臺）面積不得小於該層居室樓地板面積 5%（A-1、B-1～B-3、D-1、D-2）或 3%。')
rule(id='TC-099', law=TC, art='99', title='屋頂避難平臺', scope='project', applies="floorsAbove >= 5 && has('A-1','B-1','B-2')",
     measure='roofRefugeAreaM2', op='>=', limit='maxFloorAreaAbove5M2 / 2', unit='m²', kind='semi',
     msg='五層以上供 A-1、B-1、B-2 組使用者應設屋頂避難平臺，面積合計 ≥ 五層以上最大樓地板面積 1/2，任一邊 ≥ 6 m。')

# ───────────────────────── 第 100、104、108 條 排煙、緊急照明、緊急進口 ─────────────────────────
rule(id='TC-108', law=TC, art='108', title='二～十層緊急進口或每 10 m 開口', scope='level', kind='semi',
     where='levelIndex >= 2 && levelIndex <= 10', applies='true',
     measure='maxExteriorOpeningGapM', op='<=', limit='10', unit='m', severity='warning',
     msg='面臨道路或 4 m 以上通路且外牆每 10 m 設窗戶或開口（寬 ≥ 75 cm、高 ≥ 1.2 m 或直徑 1 m 圓孔，下緣距樓地板 ≤ 80 cm）者免設緊急進口；否則應設緊急進口（間隔 ≤ 40 m）。')

# ───────────────────────── 第 123 條 觀眾席 ─────────────────────────
rule(id='TC-123-3', law=TC, art='123', title='觀眾席天花板高度 ≥ 3.5 m、淨高 ≥ 2.5 m', scope='room', where="role == 'auditorium'", applies='true',
     measure='min(ceilingAvgMm / 3500, ceilingMinMm / 2500)', op='>=', limit='1', unit='',
     msg='觀眾席之天花板高度應在 3.5 m 以上，且淨高不得小於 2.5 m。')

# ───────────────────────── 高層建築物、工廠、老人住宅、防空避難 ─────────────────────────
rule(id='TC-241', law=TC, art='241', title='高層建築物二座以上特別安全梯', scope='project', applies='isHighRise',
     measure='specialStairBuildingCount', op='>=', limit='2', unit='座',
     msg='高層建築物應設置二座以上之特別安全梯並符合二方向避難原則。')
rule(id='TC-244', law=TC, art='244', title='高層建築物緊急昇降機', scope='project', applies='isHighRise',
     measure='emergencyElevatorCount', op='>=', limit='1', unit='座',
     msg='高層建築物地板面高度 50 m 以上或 16 層以上之樓層應設置緊急昇降機間。')
rule(id='TC-259', law=TC, art='259', title='防災中心面積 ≥ 40 m²', scope='project', applies='isHighRise',
     measure='disasterCenterAreaM2', op='>=', limit='40', unit='m²',
     msg='高層建築物應設防災中心於避難層或其直上、直下層，樓地板面積 ≥ 40 m²。')
rule(id='TC-274', law=TC, art='274', title='作業廠房淨高 ≥ 2.7 m', scope='room', where="role == 'factory'", applies='true',
     measure='clearMinMm', op='>=', limit='2700', unit='mm',
     msg='作業廠房樓層高度扣除樓板厚度及梁深後之淨高度不得小於 2.7 m。')
rule(id='TC-294', law=TC, art='294', title='老人住宅臥室 ≥ 9 m²', scope='room', where="role == 'bedroom' && in(roomUse, 'H-1') && isElderlyHousing", applies='true',
     measure='areaM2', op='>=', limit='9', unit='m²', msg='老人住宅之臥室樓地板面積應為 9 m² 以上。')
rule(id='TC-144-1', law=TC, art='144', title='防空避難設備淨高 ≥ 2.1 m', scope='room', where="role == 'shelter'", applies='true',
     measure='clearMinMm', op='>=', limit='2100', unit='mm', msg='防空避難設備天花板高度或地板至梁底高度不得小於 2.1 m。')

# ───────────────────────── 第 167 條 無障礙（技術規則本身） ─────────────────────────
rule(id='TC-167-2', law=TC, art='167-2', title='至少一座無障礙樓梯', scope='project', applies='accessibilityApplies && floorsAbove + floorsBelow > 1',
     measure='accessibleStairCount', op='>=', limit='1', unit='座', kind='semi',
     msg='建築物設置之直通樓梯至少應有一座為無障礙樓梯（以各座樓梯是否符合規範 304 判定）。')
rule(id='TC-167-3', law=TC, art='167-3', title='無障礙廁所盥洗室數量', scope='project', applies="accessibilityApplies && !has('H-2')",
     measure='accessibleToiletCount', op='>=', limit='if(floorsAbove + floorsBelow <= 3, 1, 1 + floor((floorsAbove + floorsBelow - 3 + 2) / 3))', unit='處', kind='semi',
     msg='總樓層數三層以下一處；超過部分每增加三層（且有一層 > 500 m²）加設一處，服務範圍不得大於三樓層。')
rule(id='TC-167-6', law=TC, art='167-6', title='無障礙停車位數量', scope='project', applies='parkingCount > 0',
     measure='accessibleParkingCount', op='>=', limit='requiredAccessibleParking', unit='輛',
     msg='法定停車位 50 輛以下至少一輛；51 輛以上依第 167 條之 6 附表（H-2 第一類、其他第二類）計算。')

# ───────────────────────── 建築物無障礙設施設計規範（108.07.01） ─────────────────────────
rule(id='ACC-203.2.3', law=ACC, art='203.2.3', title='室外無障礙通路寬度 ≥ 130 cm', scope='room', where="role == 'accessibleRouteOutdoor'", applies='accessibilityApplies',
     measure='minWidthMm', op='>=', limit='if(isTownhouseRule, 900, 1300)', unit='mm', kind='semi',
     msg='室外通路寬度不得小於 130 cm（獨棟連棟住宅 202.4 為 90 cm）。')
rule(id='ACC-204.2.2', law=ACC, art='204.2.2', title='室內通路走廊寬度 ≥ 120 cm', scope='room', where="role == 'corridor' && isAccessibleRoute", applies='accessibilityApplies',
     measure='minWidthMm', op='>=', limit='1200', unit='mm',
     msg='室內通路走廊寬度不得小於 120 cm，扣除門扇開啟空間後亦同。')
rule(id='ACC-204.2.3', law=ACC, art='204.2.3', title='室內通路淨高 ≥ 190 cm', scope='room', where="role == 'corridor' && isAccessibleRoute", applies='accessibilityApplies',
     measure='ceilingMinMm', op='>=', limit='1900', unit='mm', msg='室內通路走廊淨高度不得小於 190 cm。')
rule(id='ACC-204.2.4', law=ACC, art='204.2.4', title='走廊迴轉空間（寬 < 150 cm 者每 10 m）', scope='room', where="role == 'corridor' && isAccessibleRoute && minWidthMm < 1500", applies='accessibilityApplies',
     measure='lengthM', op='<=', limit='10', unit='m', kind='semi', severity='warning',
     msg='寬度小於 150 cm 之走廊，每隔 10 m、走廊盡頭或距盡頭 350 cm 以內應設直徑 150 cm 以上迴轉空間。')
rule(id='ACC-205.2.3', law=ACC, art='205.2.3', title='無障礙通路室內出入口淨寬', scope='door', where='isOnAccessibleRoute', applies='accessibilityApplies',
     measure='clearWidthMm', op='>=', limit='if(isSliding, 800, 900)', unit='mm',
     msg='門框間之距離不得小於 90 cm；橫向拉門、折疊門開啟後淨寬不得小於 80 cm。淨寬以門寬扣除門框推估。')
rule(id='ACC-206.2.2', law=ACC, art='206.2.2', title='無障礙坡道淨寬', scope='ramp', where='isAccessible', applies='accessibilityApplies',
     measure='widthMm', op='>=', limit='if(replacesStair, 1500, 900)', unit='mm',
     msg='坡道淨寬不得小於 90 cm；取代樓梯者不得小於 150 cm。')
rule(id='ACC-206.2.3', law=ACC, art='206.2.3', title='無障礙坡道坡度', scope='ramp', where='isAccessible', applies='accessibilityApplies',
     measure='slope', op='<=', cases=[["riseMm > 200", "1/12"], ["riseMm > 50", "1/10"], ["riseMm > 30", "1/5"], ["true", "1/12"]], unit='',
     msg='坡道坡度不得大於 1/12；高差 5～20 cm 者 1/10，3～5 cm 者 1/5。')
rule(id='ACC-206.3.2', law=ACC, art='206.3.2', title='坡道每高差 75 cm 設中間平台', scope='ramp', where='isAccessible', applies='accessibilityApplies',
     measure='maxRunRiseMm', op='<=', limit='750', unit='mm', msg='坡道每高差 75 cm 應設置長度 150 cm 以上之平台。')
rule(id='ACC-206.5', law=ACC, art='206.5', title='高差 > 20 cm 坡道兩側扶手', scope='ramp', where='isAccessible && riseMm > 200', applies='accessibilityApplies',
     measure='handrailCount', op='>=', limit='2', unit='側', severity='warning', msg='高差超過 20 cm 之坡道兩側應設置連續性扶手。')
rule(id='ACC-207.3.3', law=ACC, art='207.3.3', title='扶手高度', scope='railing', where='isHandrail && isAccessible', applies='accessibilityApplies',
     measure='heightMm', op='>=', limit='if(isPrimarySchool, 650, 750)', unit='mm', kind='semi', severity='warning',
     msg='單道扶手上緣距地板面 75～85 cm；雙道 65、85 cm；小學各降 10 cm。')
rule(id='ACC-304.1', law=ACC, art='304.1', title='無障礙樓梯級高 ≤ 16 cm、級深 ≥ 26 cm、55 ≤ 2R+T ≤ 65', scope='stair', where='isAccessibleStair', applies='accessibilityApplies',
     measure='if(riserMm <= if(isApartmentRule, 180, 160) && treadMm >= if(isApartmentRule, 240, 260) && twoRPlusTMm >= 550 && twoRPlusTMm <= 650, 1, 0)', op='==', limit='1', unit='',
     msg='級高 ≤ 16 cm、級深 ≥ 26 cm、55 ≤ 2R+T ≤ 65 cm（樓地板 ≤ 240 m² 之住宅公寓大廈適用 304.4：18 cm／24 cm）。')
rule(id='ACC-303.2', law=ACC, art='303.2', title='無障礙樓梯淨高 ≥ 190 cm', scope='stair', where='isAccessibleStair && headroomMinMm > 0', applies='accessibilityApplies',
     measure='headroomMinMm', op='>=', limit='1900', unit='mm', msg='樓梯梯級鼻端至樓梯間過梁之垂直淨高不得小於 190 cm。')
rule(id='ACC-305.1', law=ACC, art='305.1', title='無障礙樓梯兩側扶手', scope='stair', where='isAccessibleStair && totalRiseMm > 200', applies='accessibilityApplies',
     measure='handrailCount', op='>=', limit='2', unit='側', msg='高差超過 20 cm 之樓梯兩側應設置扶手。')
rule(id='ACC-404.1', law=ACC, art='404.1', title='昇降機出入口迴轉空間 φ150', scope='room', where="role == 'elevatorLobby'", applies='accessibilityApplies',
     measure='inscribedDiameterMm', op='>=', limit='1500', unit='mm', kind='semi',
     msg='昇降機出入口應留設直徑 150 cm 以上且坡度不大於 1/50 之淨空間。')
rule(id='ACC-406.1', law=ACC, art='406', title='無障礙昇降機門淨寬與機廂深度', scope='elevator', where='isAccessible', applies='accessibilityApplies',
     measure="min(doorWidthMm / if(has('H-2'), 800, 900), carDepthMm / if(has('H-2'), 1250, 1350))", op='>=', limit='1', unit='', kind='semi',
     msg='門淨寬 ≥ 90 cm、機廂深度 ≥ 135 cm（H-2 住宅 80 cm／125 cm）。讀取昇降機族群之門寬與機廂深度參數。')
rule(id='ACC-504.1', law=ACC, art='504.1', title='無障礙廁所迴轉空間 φ150', scope='room', where="role == 'accessibleToilet'", applies='accessibilityApplies',
     measure='inscribedDiameterMm', op='>=', limit='1500', unit='mm', kind='semi',
     msg='無障礙廁所盥洗室應設置直徑 150 cm 以上之迴轉空間（以房間內接圓近似，未扣除衛生器具）。')
rule(id='ACC-504.2', law=ACC, art='504', title='無障礙廁所橫拉門淨寬 ≥ 80 cm', scope='door', where="toRole == 'accessibleToilet' || fromRole == 'accessibleToilet'", applies='accessibilityApplies',
     measure='if(isSliding, clearWidthMm, 0)', op='>=', limit='800', unit='mm',
     msg='無障礙廁所應採用橫向拉門，出入口淨寬不得小於 80 cm。')
rule(id='ACC-604', law=ACC, art='604', title='無障礙浴室橫拉門淨寬 ≥ 80 cm', scope='door', where="toRole == 'accessibleBath' || fromRole == 'accessibleBath'", applies='accessibilityApplies',
     measure='if(isSliding, clearWidthMm, 0)', op='>=', limit='800', unit='mm', msg='無障礙浴室應採用橫向拉門，出入口淨寬不得小於 80 cm。')
rule(id='ACC-804.1', law=ACC, art='804.1', title='無障礙汽車停車位 ≥ 350 × 600 cm', scope='parking', where='isAccessible && !isMotorcycle', applies='true',
     measure='min(widthMm / 3500, lengthMm / 6000)', op='>=', limit='1', unit='',
     msg='無障礙汽車停車位長度不得小於 600 cm、寬度不得小於 350 cm（含 150 cm 下車區；相鄰共用下車區者 550 cm）。')
rule(id='ACC-805.1', law=ACC, art='805', title='無障礙機車停車位 ≥ 225 × 220 cm', scope='parking', where='isAccessible && isMotorcycle', applies='true',
     measure='min(widthMm / 2250, lengthMm / 2200)', op='>=', limit='1', unit='', msg='機車位長度不得小於 220 cm，寬度不得小於 225 cm。')
rule(id='ACC-1004.1', law=ACC, art='1004', title='無障礙客房內通路 ≥ 120 cm', scope='room', where="role == 'accessibleGuestRoom'", applies='true',
     measure='minWidthMm', op='>=', limit='1200', unit='mm', kind='semi', severity='warning',
     msg='客房內通路寬度不得小於 120 cm，床間淨寬度不得小於 90 cm。')

# ───────────────────────── 建築設備編 ─────────────────────────
rule(id='EQ-020', law=EQ, art='20', title='高度 ≥ 20 m 應設避雷設備', scope='project', applies='buildingHeightM >= 20',
     measure='lightningDeviceCount', op='>=', limit='1', unit='組', kind='semi', severity='warning',
     msg='建築物高度在 20 m 以上者應有避雷設備（以受雷部族群辨識）。')
rule(id='EQ-037', law=EQ, art='37', title='衛生設備（大便器）數量', scope='project', kind='semi',
     applies='requiredWcCount > 0', measure='wcCount', op='>=', limit='requiredWcCount', unit='個',
     msg='依建築設備編第 37 條表及使用人數計算之大便器數量（使用人數依說明欄：辦公每 m² 居室 0.1 人、其他供公眾使用 0.2 人等）。')
rule(id='EQ-115-2', law=EQ, art='115', title='昇降機房淨高', scope='room', where="role == 'elevatorMachineRoom'", applies='true',
     measure='clearMinMm', op='>=', limit='if(elevatorSpeedMpm <= 60, 2000, if(elevatorSpeedMpm <= 150, 2200, if(elevatorSpeedMpm <= 210, 2500, 2800)))', unit='mm',
     msg='機房內淨高度依昇降機設計速度：60 m/min 以下 2.0 m、150 以下 2.2 m、210 以下 2.5 m、超過 2.8 m。')

# ───────────────────────── 各類場所消防安全設備設置標準：應設置判定 ─────────────────────────
def need(id_, art, title, applies, device, msg):
    rule(id=id_, law=FS, art=art, title=title, scope='project', applies=applies,
         measure=f"deviceCount('{device}')", op='>=', limit='1', unit='具', kind='semi', msg=msg)

need('FS-014', '14', '應設滅火器',
     "fireA || fireUnderground || has('F-3') || ((fireB || fireC || fireD) && totalFloorAreaM2 >= 150) || (floorsBelow > 0 && maxBasementAreaM2 >= 50) || hasKitchenOrBoiler",
     'extinguisher', '甲類、地下建築物、幼兒園；總樓地板 ≥ 150 m² 之乙丙丁類；地下層或無開口樓層 ≥ 50 m²；設有放映室、變電、鍋爐房、廚房等場所應設滅火器。')
need('FS-015', '15', '應設室內消防栓',
     "(floorsAbove <= 5 && ((fireA1 && maxFloorAreaM2 >= 300) || ((fireA || fireB || fireC || fireD) && maxFloorAreaM2 >= 500))) || (floorsAbove >= 6 && (fireA || fireB || fireC || fireD) && maxFloorAreaM2 >= 150) || (floorsBelow > 0 && ((fireA1 && maxBasementAreaM2 >= 100) || maxBasementAreaM2 >= 150))",
     'hydrant', '五層以下甲類第一目任一層 ≥ 300 m²、其他 ≥ 500 m²；六層以上任一層 ≥ 150 m²；地下層 ≥ 100／150 m² 應設室內消防栓（設自動撒水等有效範圍內得免設）。')
need('FS-017', '17', '應設自動撒水設備',
     "(floorsAbove <= 10 && ((fireA1 && fireA1AreaM2 >= 300) || ((fireA || fireB1) && maxFloorAreaM2 >= 1500))) || (floorsAbove >= 11 && maxFloorAreaAbove10M2 >= 100) || (floorsBelow > 0 && fireA && maxBasementAreaM2 >= 1000) || (floorsAbove >= 11 && fireA) || isHighRise || fireCare",
     'sprinkler', '十層以下甲類第一目 ≥ 300 m²、甲類其他目及乙類第一目 ≥ 1,500 m²；十一層以上樓層 ≥ 100 m²；地下層甲類 ≥ 1,000 m²；高層建築物；長照等場所應設自動撒水設備。')
need('FS-019', '19', '應設火警自動警報設備',
     "(floorsAbove <= 5 && (((fireA || has('F-3')) && maxFloorAreaM2 >= 300) || ((fireB || fireC || fireD) && maxFloorAreaM2 >= 500))) || (floorsAbove >= 6 && floorsAbove <= 10 && maxFloorAreaM2 >= 300) || floorsAbove >= 11 || (floorsBelow > 0 && maxBasementAreaM2 >= 300) || (fireA && totalFloorAreaM2 >= 300) || fireCare",
     'smokeDetector', '五層以下甲類任一層 ≥ 300 m²、乙丙丁類 ≥ 500 m²；六至十層任一層 ≥ 300 m²；十一層以上；地下層 ≥ 300 m² 等應設火警自動警報設備（設自動撒水等有效範圍內得免設，甲類與高層除外）。')
need('FS-020', '20', '應設手動報警設備', "(floorsAbove >= 3 && maxFloorAreaM2 >= 200) || has('B-4')",
     'manualCallPoint', '三層以上建築物任何一層 ≥ 200 m²，或旅館等應設手動報警設備。')
need('FS-023', '23', '應設出口標示燈', "fireA || has('F-3') || floorsBelow > 0 || floorsAbove >= 11",
     'exitSign', '甲類、幼兒園等場所，或地下層、無開口樓層、十一層以上樓層應設出口標示燈及避難方向指示燈。')
need('FS-024', '24', '應設緊急照明', "fireA || has('B-4') || totalFloorAreaM2 >= 1000",
     'emergencyLight', '甲類、旅館等居室，總樓地板 ≥ 1,000 m² 建築物之居室，及其通達避難層之走廊、樓梯應設緊急照明。')
need('FS-026', '26', '應設連結送水管', "((floorsAbove == 5 || floorsAbove == 6) && totalFloorAreaM2 >= 6000) || floorsAbove >= 7",
     'standpipeOutlet', '五層或六層建築物總樓地板 ≥ 6,000 m² 及七層以上建築物應設連結送水管。')
need('FS-029', '29', '應設緊急電源插座', "floorsAbove >= 11 || emergencyElevatorCount > 0",
     'emergencyOutlet', '十一層以上建築物之各樓層及緊急昇降機間應設緊急電源插座。')

# ───────────────────────── 消防：設置位置與間距 ─────────────────────────
rule(id='FS-031-3', law=FS, art='31', title='滅火器步行距離 ≤ 20 m', scope='level', where="deviceCountOnLevel('extinguisher') > 0", applies='true',
     measure='maxWalkToDevice(\'extinguisher\')', op='<=', limit='20', unit='m',
     msg='設有滅火器之樓層，自樓面居室任一點至滅火器之步行距離在 20 m 以下。')
rule(id='FS-031-5', law=FS, art='31', title='滅火器上端高度', scope='device', where="kind == 'extinguisher'", applies='true',
     measure='topHeightMm', op='<=', limit='if(weightKg >= 18, 1000, 1500)', unit='mm', kind='semi', severity='warning',
     msg='懸掛或置於箱中之滅火器上端距樓地板面：18 kg 以上 ≤ 1 m，未滿 18 kg ≤ 1.5 m。')
rule(id='FS-034-1', law=FS, art='34', title='室內消防栓水平距離 ≤ 25 m', scope='level', where="deviceCountOnLevel('hydrant') > 0", applies='true',
     measure='maxHorizToDevice(\'hydrant\')', op='<=', limit='25', unit='m',
     msg='各層任一點至消防栓接頭之水平距離在 25 m 以下。')
rule(id='FS-034-H', law=FS, art='34', title='消防栓開關高度 0.3～1.5 m', scope='device', where="kind == 'hydrant'", applies='valveHeightMm > 0',
     measure='if(valveHeightMm >= 300 && valveHeightMm <= 1500, 1, 0)', op='==', limit='1', unit='', kind='semi', severity='warning',
     msg='消防栓開關距離樓地板之高度在 0.3 m 以上 1.5 m 以下。')
rule(id='FS-046', law=FS, art='46', title='撒水頭水平距離', scope='level', where="deviceCountOnLevel('sprinkler') > 0", applies='true',
     measure='maxHorizToDevice(\'sprinkler\')', op='<=',
     cases=[["levelHasParking", "2.1"], ["sprinklerFastResponse", "if(fireproof, 2.6, 2.3)"], ["true", "if(fireproof, 2.3, 2.1)"]],
     unit='m',
     msg='一般反應型 2.1 m（防火構造 2.3 m）；快速反應型 2.3 m（防火構造 2.6 m）；停車空間 2.1 m；免設處所（廁所、安全梯間、機房等）不計。')
rule(id='FS-122-4', law=FS, art='122', title='偵煙式探測器數量', scope='room', where='needsSmokeDetector', applies="deviceCount('smokeDetector') > 0",
     measure="deviceCountInRoom('smokeDetector')", op='>=', limit='ceil(areaM2 / if(ceilingAvgMm < 4000, 150, 75))', unit='個', kind='semi',
     msg='偵煙式探測器（一、二種）有效探測範圍：裝置面高度未滿 4 m 為 150 m²、4～20 m 為 75 m²；以房間為探測區域估算（梁區劃另計）。')
rule(id='FS-122-5', law=FS, art='122', title='走廊偵煙探測器間距 ≤ 30 m', scope='room', where="role == 'corridor'", applies="deviceCount('smokeDetector') > 0",
     measure="if(lengthM <= 10, 0, maxSpacingAlongM('smokeDetector'))", op='<=', limit='30', unit='m', kind='semi',
     msg='走廊及通道步行距離每 30 m 至少設置一個（第三種 20 m），距盡頭牆壁 15 m 以下。')
rule(id='FS-132-2', law=FS, art='132', title='火警發信機高度 1.2～1.5 m', scope='device', where="kind == 'manualCallPoint'", applies='true',
     measure='if(mountHeightMm >= 1200 && mountHeightMm <= 1500, 1, 0)', op='==', limit='1', unit='', severity='warning',
     msg='火警發信機離地板面之高度在 1.2 m 以上 1.5 m 以下。')
rule(id='FS-133-4', law=FS, art='133', title='揚聲器水平距離 ≤ 10 m', scope='level', where="deviceCountOnLevel('speaker') > 0", applies='true',
     measure='maxHorizToDevice(\'speaker\')', op='<=', limit='10', unit='m',
     msg='從各廣播區域內任一點至揚聲器之水平距離在 10 m 以下。')
rule(id='FS-146-3', law=FS, art='146-3', title='出口標示燈設於避難出入口', scope='door', where="(servesStair || isExterior) && levelRequiresExitSign", applies="deviceCount('exitSign') > 0",
     measure="nearDeviceCount('exitSign', 2.0)", op='>=', limit='1', unit='個', kind='semi',
     msg='通往戶外、直通樓梯之出入口上方或其緊鄰處應設出口標示燈（以門附近 2 m 內之出口標示燈判定）。')
rule(id='FS-153', law=FS, art='153', title='避難指標步行距離 ≤ 7.5 m', scope='room', where="role == 'corridor'", applies="deviceCount('evacuationSign') > 0",
     measure="maxHorizToDeviceInRoom('evacuationSign')", op='<=', limit='7.5', unit='m', kind='semi', severity='warning',
     msg='設於走廊或通道時，自走廊任一點至避難指標之步行距離在 7.5 m 以下。')
rule(id='FS-180-1', law=FS, art='180', title='連結送水管出水口水平距離 ≤ 50 m', scope='level', where="levelIndex >= 3 && deviceCountOnLevel('standpipeOutlet') > 0", applies='true',
     measure='maxHorizToDevice(\'standpipeOutlet\')', op='<=', limit='50', unit='m',
     msg='建築物第三層以上各層任一點至出水口之水平距離在 50 m 以下。')
rule(id='FS-191-1', law=FS, art='191', title='緊急電源插座水平距離 ≤ 50 m', scope='level', where="deviceCountOnLevel('emergencyOutlet') > 0", applies='true',
     measure='maxHorizToDevice(\'emergencyOutlet\')', op='<=', limit='50', unit='m',
     msg='每一層任何一處至緊急電源插座之水平距離在 50 m 以下。')
rule(id='FS-191-4', law=FS, art='191', title='緊急電源插座高度 1～1.5 m', scope='device', where="kind == 'emergencyOutlet'", applies='true',
     measure='if(mountHeightMm >= 1000 && mountHeightMm <= 1500, 1, 0)', op='==', limit='1', unit='', severity='warning',
     msg='緊急電源插座裝設高度距離樓地板 1 m 以上 1.5 m 以下。')

if __name__ == '__main__':
    print(len(R), 'rules')
