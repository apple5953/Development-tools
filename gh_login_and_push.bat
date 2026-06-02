@echo off
chcp 65001 >nul
echo ==================================================
echo [RTS] 正在啟動 GitHub 授權登入...
echo ==================================================
gh auth login
if %errorlevel% neq 0 goto :error

echo ==================================================
echo [RTS] 正在推送本地 main 分支程式碼至遠端儲存庫...
echo ==================================================
git push -u origin main
if %errorlevel% neq 0 goto :push_error

echo ==================================================
echo 🎉 [RTS] 恭喜！本地與 GitHub 遠端連結成功！
echo 你現在可以正常執行 release.ps1 發布你的新版本了。
echo ==================================================
pause
exit /b 0

:push_error
echo ==================================================
echo ❌ 錯誤：推送至儲存庫失敗。
echo 請先確定你已在 GitHub 上建立了名為 "Development-tools" 的空儲存庫。
echo ==================================================
pause
exit /b 1

:error
echo ==================================================
echo ❌ 錯誤：GitHub CLI 登入授權失敗。
echo 請確定你的電腦已安裝 GitHub CLI。
echo ==================================================
pause
exit /b 1
