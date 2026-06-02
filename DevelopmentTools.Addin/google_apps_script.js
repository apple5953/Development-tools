var CLIENT_ID = PropertiesService.getScriptProperties().getProperty("GOOGLE_CLIENT_ID") || "YOUR_CLIENT_ID_GOES_HERE";
var CLIENT_SECRET = PropertiesService.getScriptProperties().getProperty("GOOGLE_CLIENT_SECRET") || "YOUR_CLIENT_SECRET_GOES_HERE"; 

function doPost(e) {
  var response = { status: "error", message: "未知的伺服器錯誤" };
  try {
    var requestData = JSON.parse(e.postData.contents);
    var action = requestData.action;
    var contactInfo = "請透過 Line 聯絡作者：https://line.me/ti/p/ov08MDxYA1";

    var doc = SpreadsheetApp.getActiveSpreadsheet();
    var userSheet = doc.getSheetByName("Users");
    if (!userSheet) {
      userSheet = doc.insertSheet("Users");
      userSheet.appendRow(["Email", "Status", "AllowedTools", "RegisterTime", "LastActiveTime", "UsageCount", "TotalDurationMinutes"]);
    }

    // 取得現有標題列，動態對應欄位索引，避免欄位順序跑掉造成讀取錯誤
    var headers = userSheet.getRange(1, 1, 1, userSheet.getLastColumn()).getValues()[0];
    var emailIdx = headers.indexOf("Email");
    var statusIdx = headers.indexOf("Status");
    var allowedToolsIdx = headers.indexOf("AllowedTools");
    var registerIdx = headers.indexOf("RegisterTime");
    var lastActiveIdx = headers.indexOf("LastActiveTime");
    var usageCountIdx = headers.indexOf("UsageCount");
    var durationIdx = headers.indexOf("TotalDurationMinutes");

    if (emailIdx === -1) emailIdx = 0;
    if (statusIdx === -1) statusIdx = 1;

    // 若無 AllowedTools 欄位，自動在 Status 後面新增該欄位進行升級
    if (allowedToolsIdx === -1) {
      userSheet.insertColumnAfter(2);
      userSheet.getRange(1, 3).setValue("AllowedTools");
      headers = userSheet.getRange(1, 1, 1, userSheet.getLastColumn()).getValues()[0];
      emailIdx = headers.indexOf("Email");
      statusIdx = headers.indexOf("Status");
      allowedToolsIdx = headers.indexOf("AllowedTools");
      registerIdx = headers.indexOf("RegisterTime");
      lastActiveIdx = headers.indexOf("LastActiveTime");
      usageCountIdx = headers.indexOf("UsageCount");
      durationIdx = headers.indexOf("TotalDurationMinutes");
    }

    // 1. 中轉交換授權代碼 (Token Exchange)
    if (action === "exchange_code") {
      var code = requestData.code;
      var redirectUri = requestData.redirect_uri;
      if (!code || !redirectUri) {
        return createJsonResponse({ status: "error", message: "缺少 code 或 redirect_uri 參數。" });
      }

      var payload = {
        code: code,
        client_id: CLIENT_ID,
        client_secret: CLIENT_SECRET,
        redirect_uri: redirectUri,
        grant_type: "authorization_code"
      };

      var tokenResponse = UrlFetchApp.fetch("https://oauth2.googleapis.com/token", {
        method: "post",
        payload: payload,
        muteHttpExceptions: true
      });

      var resJson = JSON.parse(tokenResponse.getContentText());
      if (tokenResponse.getResponseCode() === 200 && resJson.access_token) {
        return createJsonResponse({
          status: "success",
          access_token: resJson.access_token,
          expires_in: resJson.expires_in
        });
      } else {
        return createJsonResponse({
          status: "error",
          message: "交換 Token 失敗：" + (resJson.error_description || tokenResponse.getContentText())
        });
      }
    }

    // 2. 需要驗證身分的所有其餘 Action
    var token = requestData.token ? requestData.token.trim() : "";
    var email = getEmailFromToken(token);

    if (!email) {
      return createJsonResponse({
        status: "error",
        message: "授權憑證無效或已過期，請重新登入 Google 帳號。"
      });
    }

    if (action === "check_auth") {
      var tool = requestData.tool ? requestData.tool.trim() : "Platform";
      var dataRange = userSheet.getDataRange();
      var values = dataRange.getValues();
      var userRowIndex = -1;

      for (var i = 1; i < values.length; i++) {
        if (values[i][emailIdx].toString().trim().toLowerCase() === email) {
          userRowIndex = i + 1; // 轉為 1-based Row Index
          break;
        }
      }

      if (userRowIndex === -1) {
        // 新使用者！自動註冊並預設開通 Tiling 權限
        var newRow = [];
        for (var c = 0; c < headers.length; c++) {
          if (c === emailIdx) newRow.push(email);
          else if (c === statusIdx) newRow.push("Allowed");
          else if (c === allowedToolsIdx) newRow.push("Tiling");
          else if (c === registerIdx) newRow.push(new Date());
          else if (c === lastActiveIdx) newRow.push(new Date());
          else if (c === usageCountIdx) newRow.push(1);
          else if (c === durationIdx) newRow.push(0);
          else newRow.push("");
        }
        userSheet.appendRow(newRow);

        response = {
          status: "success",
          message: "您的帳號已成功自動註冊，並已開通「磁磚鋪設 (Tiling)」工具的授權。",
          contact: contactInfo
        };
      } else {
        // 既有使用者
        var rowData = values[userRowIndex - 1];
        var statusVal = rowData[statusIdx].toString().trim();
        var allowedToolsVal = rowData[allowedToolsIdx] ? rowData[allowedToolsIdx].toString().trim() : "";

        // 更新最後活動時間與累計使用次數
        if (lastActiveIdx !== -1) {
          userSheet.getRange(userRowIndex, lastActiveIdx + 1).setValue(new Date());
        }
        if (usageCountIdx !== -1) {
          var curCount = parseInt(rowData[usageCountIdx]) || 0;
          userSheet.getRange(userRowIndex, usageCountIdx + 1).setValue(curCount + 1);
        }

        if (statusVal.toLowerCase() !== "allowed") {
          response = {
            status: "blocked",
            message: "您的帳號已被系統管理員停用或封鎖，無法使用任何外掛工具。",
            contact: contactInfo
          };
        } else {
          // 判定特定子工具授權
          var isToolAllowed = false;
          if (tool === "Platform") {
            isToolAllowed = true;
          } else {
            var toolsList = allowedToolsVal.split(",").map(function(t) { return t.trim().toLowerCase(); });
            if (toolsList.indexOf(tool.toLowerCase()) !== -1 || toolsList.indexOf("all") !== -1 || toolsList.indexOf("*") !== -1) {
              isToolAllowed = true;
            }
          }

          if (isToolAllowed) {
            response = {
              status: "success",
              message: "驗證成功，歡迎使用！",
              contact: contactInfo
            };
          } else {
            response = {
              status: "unauthorized",
              message: "您的帳號尚未獲得使用「" + tool + "」外掛工具的權限。",
              contact: contactInfo
            };
          }
        }
      }
    } else if (action === "submit_feedback") {
      var feedbackSheet = doc.getSheetByName("Feedback");
      if (!feedbackSheet) {
        feedbackSheet = doc.insertSheet("Feedback");
        feedbackSheet.appendRow(["Email", "Title", "Description", "Time"]);
      }
      var title = requestData.title || "";
      var description = requestData.description || "";
      feedbackSheet.appendRow([email, title, description, new Date()]);
      response = { status: "success", message: "意見反饋提交成功！" };

    } else if (action === "log_duration") {
      var durationSeconds = parseInt(requestData.durationSeconds) || 0;
      if (durationSeconds > 0) {
        var dataRange = userSheet.getDataRange();
        var values = dataRange.getValues();
        var userRowIndex = -1;
        for (var i = 1; i < values.length; i++) {
          if (values[i][emailIdx].toString().trim().toLowerCase() === email) {
            userRowIndex = i + 1;
            break;
          }
        }
        if (userRowIndex !== -1 && durationIdx !== -1) {
          var curDurationMinutes = parseFloat(values[userRowIndex - 1][durationIdx]) || 0;
          var additionalMinutes = durationSeconds / 60.0;
          userSheet.getRange(userRowIndex, durationIdx + 1).setValue(curDurationMinutes + additionalMinutes);
          response = { status: "success", message: "時長記錄成功！" };
        } else {
          response = { status: "error", message: "找不到使用者或時長欄位。" };
        }
      } else {
        response = { status: "error", message: "無效的時間參數。" };
      }
    } else {
      response = { status: "error", message: "未知的 Action。" };
    }
  } catch (err) {
    response = { status: "error", message: err.toString() };
  }

  return createJsonResponse(response);
}

// 輔助函式：透過 Google Token 請求 UserInfo API 以獲得真實 Email
function getEmailFromToken(token) {
  if (!token) return null;
  try {
    var response = UrlFetchApp.fetch("https://www.googleapis.com/oauth2/v3/userinfo", {
      headers: {
        "Authorization": "Bearer " + token
      },
      muteHttpExceptions: true
    });
    if (response.getResponseCode() === 200) {
      var userInfo = JSON.parse(response.getContentText());
      return userInfo.email ? userInfo.email.trim().toLowerCase() : null;
    }
  } catch (err) {
    console.error("Token verification failed: " + err.toString());
  }
  return null;
}

// 輔助函式：建立標準 JSON 回傳
function createJsonResponse(data) {
  return ContentService.createTextOutput(JSON.stringify(data))
    .setMimeType(ContentService.MimeType.JSON);
}
