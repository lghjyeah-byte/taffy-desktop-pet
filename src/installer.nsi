Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WinVer.nsh"
!include "x64.nsh"
Icon "${__FILEDIR__}\..\outputs\taffy.ico"
UninstallIcon "${__FILEDIR__}\..\outputs\taffy.ico"
Name "永雏塔菲桌宠"
OutFile "..\outputs\永雏塔菲桌宠_安装包_v1.4.exe"
InstallDir "$LOCALAPPDATA\Programs\TaffyDesktopPet"
InstallDirRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TaffyDesktopPet022" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
SetOverwrite on
BrandingText "永雏塔菲桌宠 v1.4"
VIProductVersion "1.4.0.0"
VIAddVersionKey /LANG=2052 "ProductName" "永雏塔菲桌宠"
VIAddVersionKey /LANG=2052 "FileDescription" "永雏塔菲桌宠安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "1.4.0"
VIAddVersionKey /LANG=2052 "LegalCopyright" "个人非官方桌宠项目"
Var TestMode
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "安装永雏塔菲桌宠"
!define MUI_WELCOMEPAGE_TEXT "将桌宠、动作图片和10条语音安装到电脑。$\r$\n$\r$\n适用于 Windows 10/11，安装到当前用户，无需管理员权限。$\r$\n$\r$\n可在后面选择桌面快捷方式和开机启动。"
!define MUI_FINISHPAGE_RUN "$INSTDIR\TaffyPet.exe"
!define MUI_FINISHPAGE_RUN_TEXT "启动永雏塔菲桌宠"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"



Section "桌宠程序、动作与语音（必需）" SEC_MAIN
  SectionIn RO
  ${If} $TestMode == 0
    FindWindow $0 "" "永雏塔菲桌宠"
    ${If} $0 != 0
      SendMessage $0 0x0010 0 0 /TIMEOUT=3000
      Sleep 300
    ${EndIf}
  ${EndIf}
  SetOutPath "$INSTDIR"
  File /r "..\work\installer-payload-v1.4\*"
  WriteINIStr "$INSTDIR\install-state.ini" "Install" "TestMode" "$TestMode"
  WriteUninstaller "$INSTDIR\卸载桌宠.exe"
  ${If} $TestMode == 0
    CreateDirectory "$SMPROGRAMS\永雏塔菲桌宠"
    CreateShortcut "$SMPROGRAMS\永雏塔菲桌宠\永雏塔菲桌宠.lnk" "$INSTDIR\TaffyPet.exe"
    CreateShortcut "$SMPROGRAMS\永雏塔菲桌宠\卸载桌宠.lnk" "$INSTDIR\卸载桌宠.exe"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TaffyDesktopPet022" "DisplayName" "永雏塔菲桌宠"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TaffyDesktopPet022" "DisplayVersion" "1.4.0"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TaffyDesktopPet022" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TaffyDesktopPet022" "UninstallString" '$\"$INSTDIR\卸载桌宠.exe$\"'
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TaffyDesktopPet022" "QuietUninstallString" '$\"$INSTDIR\卸载桌宠.exe$\" /S'
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TaffyDesktopPet022" "DisplayIcon" "$INSTDIR\TaffyPet.exe"
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TaffyDesktopPet022" "NoModify" 1
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TaffyDesktopPet022" "NoRepair" 1
  ${EndIf}
SectionEnd

Section "创建桌面快捷方式" SEC_DESKTOP
  ${If} $TestMode == 0
    CreateShortcut "$DESKTOP\永雏塔菲桌宠.lnk" "$INSTDIR\TaffyPet.exe"
  ${EndIf}
SectionEnd

Section /o "开机启动（可选）" SEC_STARTUP
  ${If} $TestMode == 0
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "TaffyDesktopPet022" '$\"$INSTDIR\TaffyPet.exe$\"'
  ${EndIf}
SectionEnd

Function .onInit
  SetShellVarContext current
  StrCpy $TestMode 0
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/TEST" $1
  ${IfNot} ${Errors}
    StrCpy $TestMode 1
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "此安装包需要 Windows 10 或更新版本。"
    Abort
  ${EndIf}
  ${If} ${RunningX64}
    SetRegView 64
  ${EndIf}
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Install"
  ${If} $0 != 1
    MessageBox MB_ICONSTOP "此电脑缺少 Windows 的 .NET Framework 4.x 完整组件。请通过 Windows 更新安装后重试。"
    Abort
  ${EndIf}
  ${If} $TestMode == 0
    ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "TaffyDesktopPet022"
    ${If} $0 != ""
      SectionSetFlags ${SEC_STARTUP} 1
    ${EndIf}
  ${EndIf}
FunctionEnd

Function .onInstSuccess
  ${If} $TestMode == 0
    SectionGetFlags ${SEC_STARTUP} $0
    IntOp $0 $0 & 1
    ${If} $0 == 0
      DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "TaffyDesktopPet022"
    ${EndIf}
  ${EndIf}
FunctionEnd
Function un.onInit
  SetShellVarContext current
  ${If} ${RunningX64}
    SetRegView 64
  ${EndIf}
  ReadINIStr $TestMode "$INSTDIR\install-state.ini" "Install" "TestMode"
FunctionEnd

Section "Uninstall"
  ${If} $TestMode != 1
    FindWindow $0 "" "永雏塔菲桌宠"
    ${If} $0 != 0
      SendMessage $0 0x0010 0 0 /TIMEOUT=3000
      Sleep 300
    ${EndIf}
    ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "TaffyDesktopPet022"
    ${If} $0 == '$\"$INSTDIR\TaffyPet.exe$\"'
      DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "TaffyDesktopPet022"
    ${EndIf}
    ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TaffyDesktopPet022" "InstallLocation"
    ${If} $0 == $INSTDIR
      DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TaffyDesktopPet022"
      Delete "$DESKTOP\永雏塔菲桌宠.lnk"
      Delete "$SMPROGRAMS\永雏塔菲桌宠\永雏塔菲桌宠.lnk"
      Delete "$SMPROGRAMS\永雏塔菲桌宠\卸载桌宠.lnk"
      RMDir "$SMPROGRAMS\永雏塔菲桌宠"
    ${EndIf}
  ${EndIf}
  Delete "$INSTDIR\TaffyPet.exe"
  Delete "$INSTDIR\taffy.ico"
  Delete "$INSTDIR\sprites.png"
  Delete "$INSTDIR\使用说明.md"
  Delete "$INSTDIR\语音来源.json"

  !macro DeleteClip ID
    Delete "$INSTDIR\audio\${ID}.wav"
  !macroend
  !insertmacro DeleteClip "01"
  !insertmacro DeleteClip "02"
  !insertmacro DeleteClip "03"
  !insertmacro DeleteClip "04"
  !insertmacro DeleteClip "05"
  !insertmacro DeleteClip "06"
  !insertmacro DeleteClip "07"
  !insertmacro DeleteClip "08"
  !insertmacro DeleteClip "09"
  !insertmacro DeleteClip "10"
  Delete "$INSTDIR\motions\Walk.png"
  Delete "$INSTDIR\motions\Pat.png"
  Delete "$INSTDIR\motions\Stretch.png"
  Delete "$INSTDIR\motions\Sit.png"
  Delete "$INSTDIR\motions\Dance.png"
  Delete "$INSTDIR\motions\Jump.png"
  Delete "$INSTDIR\motions\Sleep.png"
  Delete "$INSTDIR\motions\Drag.png"
  RMDir "$INSTDIR\audio"
  RMDir "$INSTDIR\motions"
  Delete "$INSTDIR\install-state.ini"
  Delete "$INSTDIR\卸载桌宠.exe"
  RMDir "$INSTDIR"
SectionEnd


