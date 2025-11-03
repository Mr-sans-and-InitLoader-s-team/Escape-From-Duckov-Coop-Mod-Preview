using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using System.Threading;
using System.Threading.Tasks;

namespace EscapeFromDuckovCoopMod.Chat.UI
{
    /// <summary>
    /// 独立聊天输入弹窗 - 使用Windows API实现
    /// 完全独立于Unity输入系统，避免输入冲突
    /// </summary>
    public static class ChatInputDialog
    {
        #region Windows API 声明
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetActiveWindow();
        
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
        
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();
        
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        // Windows API 常量
        private const uint MB_OK = 0x00000000;
        private const uint MB_OKCANCEL = 0x00000001;
        private const uint MB_ICONINFORMATION = 0x00000040;
        private const uint MB_ICONWARNING = 0x00000030;
        private const uint MB_TOPMOST = 0x00040000;
        private const uint MB_SETFOREGROUND = 0x00010000;
        
        private const int IDOK = 1;
        private const int IDCANCEL = 2;
        
        #endregion
        
        #region 输入对话框实现
        
        /// <summary>
        /// 显示聊天输入对话框
        /// </summary>
        /// <param name="title">对话框标题</param>
        /// <param name="prompt">提示文本</param>
        /// <param name="defaultText">默认输入文本</param>
        /// <returns>用户输入的文本，如果取消则返回null</returns>
        public static string ShowInputDialog(string title = "聊天输入", string prompt = "请输入聊天消息:", string defaultText = "")
        {
            try
            {
                Debug.Log($"[ChatInputDialog] 尝试显示输入对话框: {title}");
                
                // 优先使用PowerShell方案，更好地处理中文编码
                string result = ShowPowerShellInputBox(title, prompt, defaultText);
                
                if (result != null)
                {
                    Debug.Log($"[ChatInputDialog] PowerShell输入成功: '{result}'");
                    return result;
                }
                
                // 如果PowerShell不可用，使用VBScript回退
                result = ShowVBScriptInputBoxFallback(title, prompt, defaultText);
                
                if (result != null)
                {
                    Debug.Log($"[ChatInputDialog] VBScript输入成功: '{result}'");
                    return result;
                }
                
                // 如果都不可用，显示提示消息
                Debug.LogWarning("[ChatInputDialog] 所有输入方法都不可用，显示提示消息");
                ShowFallbackMessage();
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatInputDialog] 显示输入对话框时发生错误: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 使用PowerShell显示输入框
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="prompt">提示</param>
        /// <param name="defaultText">默认文本</param>
        /// <returns>用户输入的文本</returns>
        private static string ShowPowerShellInputBox(string title, string prompt, string defaultText)
        {
            try
            {
                // 创建PowerShell脚本，使用更好的编码处理
                string psScript = $@"
                    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                    Add-Type -AssemblyName Microsoft.VisualBasic
                    $result = [Microsoft.VisualBasic.Interaction]::InputBox('{prompt}', '{title}', '{defaultText}')
                    if ($result -ne '') {{
                        [Console]::WriteLine($result)
                    }}
                ";
                
                // 写入临时PowerShell文件，使用UTF-8 BOM编码
                string tempPsFile = System.IO.Path.GetTempFileName() + ".ps1";
                System.IO.File.WriteAllText(tempPsFile, psScript, new UTF8Encoding(true));
                
                // 执行PowerShell脚本，使用更好的编码参数
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -OutputFormat Text -File \"{tempPsFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    StandardOutputEncoding = Encoding.UTF8
                };
                
                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    process.WaitForExit(30000); // 30秒超时
                    
                    if (process.ExitCode == 0)
                    {
                        string output = process.StandardOutput.ReadToEnd().Trim();
                        
                        // 清理临时文件
                        try { System.IO.File.Delete(tempPsFile); } catch { }
                        
                        return string.IsNullOrEmpty(output) ? null : output;
                    }
                    else
                    {
                        string error = process.StandardError.ReadToEnd();
                        Debug.LogWarning($"[ChatInputDialog] PowerShell执行失败: {error}");
                        
                        // 清理临时文件
                        try { System.IO.File.Delete(tempPsFile); } catch { }
                        
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatInputDialog] PowerShell输入框错误: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// VBScript输入框回退方案
        /// </summary>
        private static string ShowVBScriptInputBoxFallback(string title, string prompt, string defaultText)
        {
            try
            {
                // 使用改进的VBScript，处理中文编码
                string vbScript = $@"
                    Option Explicit
                    Dim result, fso, file
                    result = InputBox(""{prompt}"", ""{title}"", ""{defaultText}"")
                    If result <> """" Then
                        Set fso = CreateObject(""Scripting.FileSystemObject"")
                        Set file = fso.CreateTextFile(""{System.IO.Path.GetTempPath()}vbs_output.txt"", True)
                        file.WriteLine result
                        file.Close
                    End If
                ";
                
                // 写入临时VBS文件，使用UTF-8编码
                string tempVbsFile = System.IO.Path.GetTempFileName() + ".vbs";
                string outputFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vbs_output.txt");
                
                // 清理可能存在的输出文件
                try { System.IO.File.Delete(outputFile); } catch { }
                
                System.IO.File.WriteAllText(tempVbsFile, vbScript, Encoding.UTF8);
                
                // 执行VBScript
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cscript.exe",
                    Arguments = $"//NoLogo \"{tempVbsFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                
                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    process.WaitForExit(30000); // 30秒超时
                    
                    // 从文件读取结果，避免控制台编码问题
                    if (System.IO.File.Exists(outputFile))
                    {
                        string output = System.IO.File.ReadAllText(outputFile, Encoding.UTF8).Trim();
                        
                        // 清理临时文件
                        try { System.IO.File.Delete(tempVbsFile); } catch { }
                        try { System.IO.File.Delete(outputFile); } catch { }
                        
                        return string.IsNullOrEmpty(output) ? null : output;
                    }
                }
                
                // 清理临时文件
                try { System.IO.File.Delete(tempVbsFile); } catch { }
                try { System.IO.File.Delete(outputFile); } catch { }
                
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatInputDialog] VBScript回退输入框错误: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 使用Windows Forms显示输入对话框
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="prompt">提示</param>
        /// <param name="defaultText">默认文本</param>
        /// <returns>用户输入的文本</returns>
        private static string ShowWindowsFormsInputDialog(string title, string prompt, string defaultText)
        {
            try
            {
                // 由于Unity的限制，我们不能直接使用Windows Forms
                // 这里返回null表示不可用
                Debug.Log("[ChatInputDialog] Windows Forms在Unity中不可用");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatInputDialog] Windows Forms输入对话框错误: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 显示降级提示消息
        /// </summary>
        private static void ShowFallbackMessage()
        {
            try
            {
                IntPtr gameWindow = GetActiveWindow();
                string message = "聊天输入对话框不可用。\n\n" +
                               "请使用游戏内的聊天输入框：\n" +
                               "1. 按 Enter 键打开聊天输入\n" +
                               "2. 在输入框中输入消息\n" +
                               "3. 点击发送按钮发送消息";
                
                uint flags = MB_OK | MB_ICONINFORMATION | MB_TOPMOST;
                MessageBox(gameWindow, message, "聊天系统", flags);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatInputDialog] 显示降级消息错误: {ex.Message}");
            }
        }
        
        #endregion
        
        #region 高级输入对话框（备用实现）
        
        /// <summary>
        /// 显示高级输入对话框（需要额外的Windows Forms引用）
        /// 这是一个备用实现，当前使用简化版本
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="prompt">提示</param>
        /// <param name="defaultText">默认文本</param>
        /// <returns>输入的文本</returns>
        public static string ShowAdvancedInputDialog(string title, string prompt, string defaultText = "")
        {
            // 注意：这个方法需要引用System.Windows.Forms
            // 由于Unity项目的限制，我们暂时使用简化版本
            
            try
            {
                // 这里可以实现更复杂的输入对话框
                // 例如使用Windows Forms或WPF
                
                Debug.Log($"[ChatInputDialog] 高级输入对话框请求: {title} - {prompt}");
                
                // 暂时返回null，表示功能未实现
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatInputDialog] 高级输入对话框错误: {ex.Message}");
                return null;
            }
        }
        
        #endregion
        
        #region 工具方法
        
        /// <summary>
        /// 显示信息消息框
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <param name="title">标题</param>
        public static void ShowInfo(string message, string title = "聊天系统")
        {
            try
            {
                IntPtr gameWindow = GetActiveWindow();
                uint flags = MB_OK | MB_ICONINFORMATION | MB_TOPMOST;
                MessageBox(gameWindow, message, title, flags);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatInputDialog] 显示信息对话框错误: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 显示警告消息框
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <param name="title">标题</param>
        public static void ShowWarning(string message, string title = "聊天系统警告")
        {
            try
            {
                IntPtr gameWindow = GetActiveWindow();
                uint flags = MB_OK | MB_ICONWARNING | MB_TOPMOST;
                MessageBox(gameWindow, message, title, flags);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatInputDialog] 显示警告对话框错误: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 显示确认对话框
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <param name="title">标题</param>
        /// <returns>用户是否点击确定</returns>
        public static bool ShowConfirm(string message, string title = "聊天系统确认")
        {
            try
            {
                IntPtr gameWindow = GetActiveWindow();
                uint flags = MB_OKCANCEL | MB_ICONINFORMATION | MB_TOPMOST;
                int result = MessageBox(gameWindow, message, title, flags);
                return result == IDOK;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatInputDialog] 显示确认对话框错误: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 验证输入文本
        /// </summary>
        /// <param name="input">输入文本</param>
        /// <param name="maxLength">最大长度</param>
        /// <returns>验证是否通过</returns>
        public static bool ValidateInput(string input, int maxLength = 200)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                ShowWarning("消息内容不能为空！");
                return false;
            }
            
            if (input.Length > maxLength)
            {
                ShowWarning($"消息长度不能超过 {maxLength} 个字符！\n当前长度: {input.Length}");
                return false;
            }
            
            return true;
        }
        
        #endregion
    }
}