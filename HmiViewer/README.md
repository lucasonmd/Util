# HMI Viewer — C# WPF (.NET 8.0) 전술 HMI 뷰어

## 프로젝트 구조

```
HmiViewer/
├── HmiViewer.sln
└── HmiViewer/
    ├── HmiViewer.csproj
    ├── App.xaml / App.xaml.cs
    ├── Commands/
    │   └── RelayCommand.cs          # ICommand 범용 구현
    ├── Models/
    │   ├── ChannelModel.cs          # 채널 데이터 (INotifyPropertyChanged)
    │   ├── HmiButtonModel.cs        # 버튼 레이블 + VK 코드
    │   └── ConnectionState.cs       # Disconnected / Ready / Running enum
    ├── ViewModels/
    │   ├── ViewModelBase.cs         # SetProperty / RaisePropertyChanged
    │   ├── ChannelViewModel.cs      # 채널 상태 + 영상 소스 확장 지점
    │   └── MainViewModel.cs         # 전체 앱 상태 관리
    ├── Views/
    │   ├── MainWindow.xaml          # 전체 레이아웃
    │   └── MainWindow.xaml.cs
    ├── Controls/
    │   ├── VideoSlotControl.xaml    # 재사용 가능한 영상 슬롯 UserControl
    │   └── VideoSlotControl.xaml.cs
    ├── Services/
    │   └── InputService.cs          # Win32 SendInput 기반 키 이벤트 전송
    └── Resources/
        ├── Colors.xaml              # 색상 팔레트 (군용/전술 스타일)
        └── Styles.xaml              # 버튼, 텍스트박스, 패널 스타일
```

## 레이아웃 구성

```
┌──────────────────────────────────────────────────────────────┐
│  [FUNC1][FUNC2]...[FUNC8]    ■ TITLE ■    [IP____][RUN][STAT]│
├──────┬───────────────────────────────┬──────┬────────────────┤
│  F1  │                               │  F7  │  ┌──────────┐  │
│  F2  │        MAIN VIDEO             │  F8  │  │  CH 01 ◀│  │
│  F3  │     (Transparent)             │  F9  │  ├──────────┤  │
│  F4  │   CH 01 (선택 채널명 표시)    │  F10 │  │  CH 02  │  │
│  F5  │                               │  F11 │  ├──────────┤  │
│  F6  │                               │  F12 │  │  CH 03  │  │
│      │                               │      │  ├──────────┤  │
│      │                               │      │  │  CH 04  │  │
│      │                               │      │  ├──────────┤  │
│      │                               │      │  │  CH 05  │  │
│      │                               │      │  └──────────┘  │
├──────┴───────────────────────────────┴──────┴────────────────┤
│  [F13][F14][F15][F16][F17][F18][F19][F20]                    │
└──────────────────────────────────────────────────────────────┘
```

## 핵심 기능

### 채널 선택
- 우측 CH 버튼 클릭 → `SelectChannelCommand` 실행
- 선택된 채널: 청록 테두리 + 좌측 인디케이터 바 강조
- 메인 화면 좌상단에 선택 채널명 표시

### 키보드 이벤트 전송 (Win32 SendInput)
```csharp
// InputService.cs
InputService.TargetWindowHandle = /* 대상 창 HWND */;
InputService.SendVirtualKey(vkCode);
```
- 버튼 클릭 → `SendKeyCommand(HmiButtonModel)` → `InputService.SendVirtualKey(vk)`
- RUN 상태일 때만 전송 (`CanExecute` 제어)

### 연결 상태 관리
| 상태          | 색상   | 조건                        |
|---------------|--------|-----------------------------|
| DISCONNECTED  | 빨강   | 기본 / IP 오류              |
| READY         | 노랑   | IP 유효, 연결 준비          |
| RUNNING       | 초록   | RUN 버튼 클릭 후            |

- IP 유효성 검사: `IPAddress.TryParse` 사용
- 잘못된 IP → RUN 버튼 자동 비활성화

## 향후 실제 영상 연동 확장

### D3D / HWND 삽입 방법

**방법 1: HwndHost (Win32 HWND 직접 삽입)**
```csharp
// ChannelViewModel.VideoSource에 HwndHost 파생 클래스 할당
public class VideoHwndHost : HwndHost {
    protected override HandleRef BuildWindowCore(HandleRef hwndParent) {
        // CreateWindowEx로 자식 창 생성 후 HWND 반환
    }
}
```

**방법 2: D3DImage (DirectX Surface)**
```csharp
D3DImage d3dImg = new();
d3dImg.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surfacePtr);
// VideoSlotControl.VideoContent = new Image { Source = d3dImg };
```

**방법 3: MediaElement (WPF 내장)**
```csharp
var media = new MediaElement { Source = new Uri("rtsp://...") };
// VideoSlotControl.VideoContent = media;
```

### 확장 지점 요약
- `ChannelViewModel.VideoSource` — 영상 소스 객체 보관
- `VideoSlotControl.VideoContent` — UIElement 주입 프로퍼티
- `MainWindow.MainVideoHost` — 메인 영상 영역 Border (코드비하인드에서 접근 가능)
- `InputService.TargetWindowHandle` — 키 이벤트 대상 창 HWND

## 빌드 요구사항
- .NET 8.0 Windows
- Visual Studio 2022+ 또는 `dotnet build`
- `AllowUnsafeBlocks=true` (P/Invoke용)
