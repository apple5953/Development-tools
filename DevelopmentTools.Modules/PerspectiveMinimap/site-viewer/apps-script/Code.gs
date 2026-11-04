/**
 * BIM 現場檢核點檢視器 (Google Apps Script Web App) — MVP v0.1
 *
 * 部署：
 *   1. Script Properties 設定 ROOT_FOLDER_ID = Drive「BIM現場檢核/<專案>」資料夾 id
 *   2. 部署 > 新增部署 > 網頁應用程式：以「我」的身分執行；存取權依現場需求選擇
 *   3. QR Code 內容：<Web App URL>?s=<stationId>&t=<token>
 *
 * Drive 結構（由 Revit 外掛輸出、Google Drive 桌面版同步）：
 *   ROOT/<stationId>/station.json
 *   ROOT/<stationId>/perspective.jpg
 *   ROOT/<stationId>/locator.png      (選用)
 *
 * 不在此存放任何憑證；token 只做「不可猜網址」用途，不是身分驗證。
 */

var MAX_IMAGE_BYTES = 4 * 1024 * 1024;

function doGet(e) {
  var params = (e && e.parameter) || {};
  var stationId = String(params.s || '');
  var token = String(params.t || '');

  try {
    if (!/^[A-Za-z0-9_\-]{1,64}$/.test(stationId) || !token) {
      return renderError_('QR Code 內容不完整，請重新掃描。');
    }

    var root = getRootFolder_();
    var folder = findChildFolder_(root, stationId);
    if (!folder) return renderError_('找不到檢核點 ' + stationId + '。');

    var station = readJson_(folder, 'station.json');
    if (!station || !safeEquals_(String(station.token || ''), token)) {
      return renderError_('檢核點連結無效或已更新，請掃描現場最新 QR Code。');
    }

    var view = {
      station: sanitizeStation_(station),
      perspective: readImageDataUri_(folder, (station.images && station.images.perspective) || 'perspective.jpg'),
      locator: readImageDataUri_(folder, (station.images && station.images.locator) || 'locator.png')
    };

    var tpl = HtmlService.createTemplateFromFile('Index');
    tpl.viewJson = toScriptJson_(view);
    return tpl.evaluate()
      .setTitle('檢核點 ' + stationId)
      .addMetaTag('viewport', 'width=device-width, initial-scale=1, maximum-scale=5');
  } catch (err) {
    console.error(err);
    return renderError_('讀取檢核點時發生錯誤，請稍後再試。');
  }
}

function getRootFolder_() {
  var id = PropertiesService.getScriptProperties().getProperty('ROOT_FOLDER_ID');
  if (!id) throw new Error('ROOT_FOLDER_ID 未設定');
  return DriveApp.getFolderById(id);
}

function findChildFolder_(parent, name) {
  var it = parent.getFoldersByName(name);
  return it.hasNext() ? it.next() : null;
}

function findFile_(folder, name) {
  var it = folder.getFilesByName(name);
  return it.hasNext() ? it.next() : null;
}

function readJson_(folder, name) {
  var f = findFile_(folder, name);
  if (!f) return null;
  return JSON.parse(f.getBlob().getDataAsString('UTF-8'));
}

function readImageDataUri_(folder, name) {
  if (!name || name.indexOf('/') >= 0) return null;
  var f = findFile_(folder, name);
  if (!f) return null;
  var blob = f.getBlob();
  if (blob.getBytes().length > MAX_IMAGE_BYTES) return null;
  return 'data:' + blob.getContentType() + ';base64,' + Utilities.base64Encode(blob.getBytes());
}

/** 只把頁面需要的欄位送到前端，不回傳 token。 */
function sanitizeStation_(s) {
  return {
    stationId: s.stationId || '',
    project: s.project || '',
    level: s.level || {},
    eye: s.eye || {},
    direction: s.direction || {},
    note: s.note || '',
    modelRevision: s.modelRevision || {}
  };
}

function safeEquals_(a, b) {
  if (a.length !== b.length) return false;
  var diff = 0;
  for (var i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

function renderError_(message) {
  var tpl = HtmlService.createTemplateFromFile('Index');
  tpl.viewJson = toScriptJson_({ error: message });
  return tpl.evaluate()
    .setTitle('檢核點')
    .addMetaTag('viewport', 'width=device-width, initial-scale=1');
}

/** 內嵌到 <script> 的 JSON，跳脫 < 避免 </script> 注入。 */
function toScriptJson_(obj) {
  return JSON.stringify(obj).replace(/</g, '\\u003c').replace(/\u2028/g, '\\u2028').replace(/\u2029/g, '\\u2029');
}
