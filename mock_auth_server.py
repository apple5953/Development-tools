import http.server
import json
import urllib.request
import urllib.error

# 模擬資料庫 (全域變數以在多個 POST 請求之間保持狀態)
USERS_DB = {
    "dimadima5953@gmail.com": {
        "status": "Allowed",
        "allowed_tools": ["Tiling"]
    },
    "admin@example.com": {
        "status": "Allowed",
        "allowed_tools": ["Tiling", "WallFinish"]
    },
    "blocked_user@example.com": {
        "status": "Blocked",
        "allowed_tools": []
    }
}

class MockAuthHandler(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        content_length = int(self.headers['Content-Length'])
        post_data = self.rfile.read(content_length)
        
        try:
            data = json.loads(post_data.decode('utf-8'))
            action = data.get('action', '')
            
            print(f"[伺服器] 收到請求 - Action: {action}")
            contact_info = "請透過 Line 聯絡作者：https://line.me/ti/p/ov08MDxYA1"
            
            # 1. 處理 Token 交換
            if action == "exchange_code":
                code = data.get('code', '')
                redirect_uri = data.get('redirect_uri', '')
                if not code:
                    response = {"status": "error", "message": "code cannot be empty"}
                    self.send_response(400)
                else:
                    # 模擬交換 Token，將 code 包裝在 token 中以供後續解析
                    response = {
                        "status": "success",
                        "access_token": f"mock_access_token_{code}",
                        "expires_in": 3600
                    }
                    self.send_response(200)
            
            # 2. 處理其它需要身分認證的 action
            elif action in ["check_auth", "submit_feedback", "log_duration"]:
                token = data.get('token', '')
                email = None
                
                # 認證 Token 獲取 Email 邏輯
                if not token:
                    response = {"status": "error", "message": "Token cannot be empty"}
                    self.send_response(400)
                else:
                    if token.startswith("mock_access_token_"):
                        # 解析 mock token，將後方 code 直接視為 email (方便模擬登入)
                        email = token.replace("mock_access_token_", "").strip().lower()
                    elif '@' in token:
                        # 方便直接用 email 作為 token 測試
                        email = token.strip().lower()
                    else:
                        # 在線模式：嘗試打 Google UserInfo API 驗證真實 Token
                        try:
                            req = urllib.request.Request("https://www.googleapis.com/oauth2/v3/userinfo")
                            req.add_header('Authorization', f'Bearer {token}')
                            with urllib.request.urlopen(req, timeout=5) as res:
                                if res.status == 200:
                                    user_info = json.loads(res.read().decode('utf-8'))
                                    email = user_info.get('email', '').strip().lower()
                        except Exception as ex:
                            print(f"[伺服器] 線上驗證 Google Token 失敗: {ex}")
                            email = None
                    
                    if not email:
                        response = {"status": "error", "message": "授權憑證無效或已過期，請重新登入。"}
                        self.send_response(200) # 仍回傳 200 但帶 error message
                    else:
                        print(f"[伺服器] 驗證成功 - 解析出 Email: {email}")
                        
                        if action == "check_auth":
                            tool = data.get('tool', 'Platform')
                            if email not in USERS_DB:
                                # 首次登入，自動註冊並設為 Allowed，開通 Tiling 權限
                                USERS_DB[email] = {
                                    "status": "Allowed",
                                    "allowed_tools": ["Tiling"]
                                }
                                response = {
                                    "status": "success", 
                                    "message": "您的帳號已自動註冊並開通「磁磚鋪設 (Tiling)」工具的授權。",
                                    "contact": contact_info
                                }
                                self.send_response(200)
                            else:
                                user_info = USERS_DB[email]
                                if user_info["status"] != "Allowed":
                                    response = {
                                        "status": "blocked",
                                        "message": "您的帳號已被系統管理員停用或封鎖，無法使用任何外掛工具。",
                                        "contact": contact_info
                                    }
                                    self.send_response(200)
                                else:
                                    # 判定特定工具授權
                                    is_allowed = False
                                    if tool == "Platform":
                                        is_allowed = True
                                    elif tool.lower() in [t.lower() for t in user_info["allowed_tools"]]:
                                        is_allowed = True
                                        
                                    if is_allowed:
                                        response = {
                                            "status": "success",
                                            "message": "驗證成功，歡迎使用！",
                                            "contact": contact_info
                                        }
                                    else:
                                        response = {
                                            "status": "unauthorized",
                                            "message": f"您的帳號尚未獲得使用「{tool}」外掛工具的權限。",
                                            "contact": contact_info
                                        }
                                    self.send_response(200)
                        elif action == "submit_feedback":
                            response = {"status": "success", "message": "意見反饋提交成功！"}
                            self.send_response(200)
                        elif action == "log_duration":
                            response = {"status": "success", "message": "時長記錄成功！"}
                            self.send_response(200)
            else:
                response = {"status": "error", "message": f"未知的 Action: {action}"}
                self.send_response(400)
                
        except Exception as e:
            import traceback
            traceback.print_exc()
            response = {"status": "error", "message": str(e)}
            try:
                self.send_response(500)
            except:
                pass
            
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(json.dumps(response).encode('utf-8'))

def run(port=8080):
    server_address = ('', port)
    httpd = http.server.HTTPServer(server_address, MockAuthHandler)
    print(f"==================================================")
    print(f" Development tools 本地授權測試伺服器已啟動！")
    print(f" 監聽網址: http://localhost:{port}/")
    print(f" 測試時，請將 platform_config.json 中的 GoogleSheetApiUrl 改為:")
    print(f" \"http://localhost:{port}/\"")
    print(f"")
    print(f" 模擬用戶名單與權限：")
    print(f" 1. dimadima5953@gmail.com -> 僅授權 [Tiling] (無法使用自動粉刷)")
    print(f" 2. admin@example.com       -> 授權 [Tiling, WallFinish] (兩者皆可使用)")
    print(f" 3. blocked_user@example.com -> 全域封鎖 Blocked (無法使用任何工具)")
    print(f" 4. 任意新 Email             -> 自動註冊並開通 [Tiling] 權限")
    print(f"==================================================")
    httpd.serve_forever()

if __name__ == '__main__':
    run()
