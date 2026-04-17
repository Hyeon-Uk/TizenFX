# Tizen.Applications.Team 모듈 코드 리뷰

## 개요

이번 리뷰의 대상은 `tam/main_TAM` 브랜치에 새로 추가된 `src/Tizen.Applications.Team/` 모듈로, 단일 호스트 프로세스 안에서 여러 멤버 어플리케이션(이른바 "Team" 멤버)을 동적으로 로드/언로드해 실행하는 신규 런타임이다. 핵심 구조는 `TeamLoop`(네이티브 메인루프 래퍼) + `TeamManager`(전역 레지스트리) + `TeamApplication` 계층(Core/UI/View/Service) + `TeamCoreBackend` 계층으로 잘 분리되어 있고, 기존 `Tizen.Applications.Common`의 `CoreApplication` / `CoreUIApplication` 계층과 일관된 명명 규칙(`OnCreate`, `OnTerminate`, `Run`, `Exit` 등)을 따르고 있어 사용자 입장에서의 학습 곡선은 낮다. 모든 공개 API는 `[EditorBrowsable(Never)]`로 가려져 있어 ACR 전 단계에서 inhouse 노출 정책도 잘 지켜지고 있다.

다만 모듈 자체가 **dynamic AssemblyLoadContext + native callback + GLib main loop**라는 비교적 위험도 높은 영역을 다루고 있고, 그에 따라 (1) 네이티브 핸들과 델리게이트 수명관리, (2) IDisposable 패턴의 일관성, (3) 멀티 멤버 동시 실행 환경에서의 thread-safety, (4) 네임스페이스 중복(특히 `ResourceControl`)이 핵심 리스크로 떠오른다. 또한 `TeamUiApplication.Run()`이 자기 자신만 호출하는 등 간단한 코딩 실수도 몇 군데 보였다.

전반적으로 "동작은 하지만 production-grade까지 가려면 한 차례 더 정리가 필요하다"는 평가가 가능하다. 아래 섹션에서 발견된 사례를 우선순위별로 정리한다. 발견된 이슈는 총 22개이며, 그중 High 8개, Medium 9개, Low 5개로 분류했다.

## 1. 코드 실수 및 잠재 버그

### 1.1 `TeamCoreApplication.OnCreate` 흐름에서 기본 윈도우가 숨김 상태로 남음
**파일**: `src/Tizen.Applications.Team/TeamCoreApplication.cs:135-150`
**문제**: `TeamUICoreBackend.OnCreateNative` 안에서 `window = new Window()` → `SetDefaultWindow(window)` → 사용자 핸들러 invoke → `CreateWl2WindowById` 순서로 초기화된다. inner try-catch가 정상 종료되더라도 `window`는 `Hide()`된 상태이므로 기본 윈도우가 화면에 안 뜬다. 의도된 동작이라면 문서에 분명히 표시할 필요가 있다.
**영향**: Created 이벤트 핸들러에서 별도로 `GetDefaultWindow().Show()`를 호출하지 않으면 화면이 표시되지 않는데, 문서에는 명시되어 있지 않다.
**권장**: `TeamUICoreBackend.OnCreateNative`의 `window.Hide()` 직전/직후에 "user must explicitly Show() the window" 주석을 추가하거나, 또는 사용자 핸들러 종료 후 자동으로 `Show()`하도록 하라.

### 1.2 `TeamUiApplication.Run`이 `base.Run(args)` 외에 어떤 추가 작업도 하지 않음 — 사실상 무용 메서드
**파일**: `src/Tizen.Applications.Team/TeamUiApplication.cs:45-48`
**문제**:
```csharp
public override void Run(string[] args)
{
    base.Run(args);
}
```
`override`만 해놓고 동일하게 `base.Run`을 호출한다. 역할이 전혀 없다. 단순 forwarding `override`는 추후 `base` 구현이 바뀌어도 호출자가 깨닫지 못하게 만들고, `[EditorBrowsable(Never)]` 표시도 일관되지 않게 만든다.
**영향**: 코드 가독성 저하 및 IL 크기 증가. 또한 `TeamUiApplication`의 생성자(`public TeamUiApplication() { }`)가 비어 있어 `TeamCoreUiApplication`과 사실상 차이가 없는데, 클래스를 분리한 이유가 모호하다.
**권장**: `Run()` 오버라이드를 제거하거나, `TeamUiApplication` 자체를 제거하고 `TeamCoreUiApplication`을 사용하도록 통합하라.

### 1.3 `TeamUICoreBackend.Run` 등의 `argsClone` 길이/복사 인덱스 오류
**파일**: `src/Tizen.Applications.Team/Tizen.Applications.CoreBackend/TeamUICoreBackend.cs:111-117`, 동일 패턴이 `TeamServiceCoreBackend.cs:84-90`, `TeamViewCoreBackend.cs:111-117`에도 존재
**문제**:
```csharp
string[] argsClone = new string[args == null ? 1 : args.Length + 1];
if (args != null && args.Length > 1)
{
    args.CopyTo(argsClone, 1);
}
argsClone[0] = "Tizen.Applications.Team.dll";
```
- `args.Length > 1` 조건이 잘못되었다. `args.Length >= 1`이어야 한다. `args.Length == 1`일 때 그 한 개의 인자가 사라진다.
- 결국 `args.Length == 1`인 경우에만 사용자 인자가 무시되는 미묘한 버그.

**영향**: 명령줄 인자 한 개를 전달했을 때 그 인자가 silently 무시된다.
**권장**:
```csharp
if (args != null && args.Length >= 1)
{
    args.CopyTo(argsClone, 1);
}
```
또는 더 간단히 `Array.Copy(args, 0, argsClone, 1, args.Length);`로 바꾸어라. 세 backend 파일 모두 동일하게 수정 필요.

### 1.4 `TeamApplication.Dispose(bool)`가 finalizer에서 `Exit()` 호출 → managed 객체 접근 위험
**파일**: `src/Tizen.Applications.Team/TeamApplication.cs:223-238`
**문제**:
```csharp
protected virtual void Dispose(bool disposing)
{
    if (!_disposedValue)
    {
        if (disposing)
        {
            if (_applicationInfo != null) _applicationInfo.Dispose();
            Exit();
        }
        _disposedValue = true;
    }
}
```
`Exit()`은 `disposing == true`일 때만 호출되고 있어 finalizer에서는 native 핸들 정리(`_backend.Exit()`)가 일어나지 않는다. 그런데 `_backend`가 들고 있는 native 핸들(`_memberHandle`)은 unmanaged 자원이므로 finalizer에서도 정리되어야 한다. 결과적으로 사용자가 `Dispose()`를 호출하지 않으면 `team_member_quit` 같은 native cleanup이 영원히 호출되지 않는 leak이 발생할 수 있다.
**영향**: 멤버 어플리케이션을 명시적으로 Dispose하지 않는 사용자 코드에서 native 멤버 핸들 누수.
**권장**: `Exit()`을 `disposing` 여부와 무관하게(다만 `_backend`가 이미 disposed된 경우만 회피) 호출하거나, 또는 `_backend`의 Dispose 흐름과 일치시키도록 `_backend.Dispose()`를 호출하라. 현재 `TeamCoreBackend`는 `DefaultCoreBackend`를 상속하므로 `Dispose(bool)`가 이미 정의되어 있다.

### 1.5 `TeamLoop.DoCreateArgs`의 `mainMethod.Name` 접근 시 NRE 가능성
**파일**: `src/Tizen.Applications.Team/TeamLoop.cs:179-186`
**문제**:
```csharp
var mainMethod = assemblyInfo.Assembly.EntryPoint;
Log.Info(LogTag, $"Main method {mainMethod.Name}");

if (mainMethod == null)
{
    Log.Error(LogTag, $"Entry point not found in assembly: {assemblyInfo.Path}");
    return IntPtr.Zero;
}
```
`mainMethod`가 null이면 로그 라인에서 NRE가 발생한다. null 체크가 로그 호출 후에 위치한다.
**영향**: EntryPoint가 없는 assembly에서 어플리케이션이 즉시 죽는다 (assembly가 라이브러리 형태로 배포된 경우 흔히 발생).
**권장**: null 체크를 로그 호출 전에 하라.

### 1.6 `TeamLoop.DoUnload`에서 `LoadContextRef.IsAlive` 체크 후 Target 사용 race
**파일**: `src/Tizen.Applications.Team/TeamLoop.cs:143-148`
**문제**: `IsAlive`로 체크한 후 `LoadContextRef.Target`을 가져오는 사이에 GC가 일어나면 `Target`이 null이 되어 `context.Unload()`에서 NRE가 발생한다.
**영향**: 흔하지 않지만 GC 타이밍에 따라 unload 흐름에서 crash 가능.
**권장**: `Target`을 한 번에 받아 null 체크를 한다.
```csharp
var context = assemblyInfo.LoadContextRef.Target as TeamAssemblyLoadContext;
if (context != null) { context.Unload(); ... } else { Log.Warn(...); }
```

### 1.7 `TeamLoop.DoLoad`에서 `WeakReference`로 잡았지만 `AssemblyLoadContext`를 직접 보관하지 않아 즉시 collect 가능
**파일**: `src/Tizen.Applications.Team/TeamLoop.cs:108-113`
**문제**:
```csharp
TeamAssemblyLoadContext context = new TeamAssemblyLoadContext();
WeakReference contextRef = new WeakReference(context);
Assembly assembly = context.LoadFromAssemblyPath(path);
var info = new AssemblyInfo(assembly, path, contextRef);
IntPtr id = TeamManager.RegisterAssemblyInfo(info);
```
`context` 로컬 변수가 메서드 종료 시 사라지면, 등록된 `AssemblyInfo`는 `WeakReference`만 가지고 있으므로 GC가 일어나면 `LoadContextRef.Target`이 null이 된다. assembly 자체는 `info.Assembly`에서 strong ref로 잡혀 있으나 `AssemblyLoadContext`는 잡히지 않는다.
실제로 `AssemblyLoadContext`가 collect되면 그 안에서 로드된 assembly 메타도 정리될 수 있어 후속 호출에서 문제가 된다.
**영향**: 동적 로드된 멤버 assembly가 예상보다 빨리 unload 되는 잠재적 버그. 특히 GC Mode가 server인 환경에서 더 자주 발생.
**권장**: `AssemblyInfo`가 `AssemblyLoadContext`를 strong reference로 보관하도록 한다. Unload할 때 strong ref를 null로 만들고, 그 후에 GC를 강제하면 `WeakReference.IsAlive`로 collect 여부를 확인하는 collectible ALC 표준 패턴이 완성된다.

### 1.8 `TeamCoreApplication.Post<T>(Func<T>)`의 `task.SetResult(runner())`가 사용자 throw 시 task가 영원히 미완료
**파일**: `src/Tizen.Applications.Team/TeamCoreApplication.cs:312-314`
**문제**:
```csharp
GSourceManager.Post(() => { task.SetResult(runner()); } );
```
`runner()`가 throw하면 `SetResult`가 호출되지 않아 `await task.Task`는 영원히 hang한다. `GSourceManager.ProcessQueue()`는 `action?.Invoke()`만 하고 예외를 GSource 내부로 전파하므로 caller쪽에서 catch가 일어나지 않는다.
**영향**: `Post<T>`로 던진 작업이 던지면 호출자가 영원히 await에서 멈춤(deadlock).
**권장**:
```csharp
GSourceManager.Post(() =>
{
    try { task.SetResult(runner()); }
    catch (Exception ex) { task.SetException(ex); }
});
```

## 2. 메모리 관련 이슈

### 2.1 `TeamApplicationInfo.Dispose(bool)`가 IDisposable 패턴 비표준 구현
**파일**: `src/Tizen.Applications.Team/TeamApplicationInfo.cs:584-599`
**문제**:
```csharp
#pragma warning disable CA1063
private void Dispose(bool disposing)
#pragma warning restore CA1063
{
    if (_disposed) return;
    if (disposing) { }
    if (_infoHandle != IntPtr.Zero) { Interop.ApplicationManager.AppInfoDestroy(_infoHandle); ... }
    _disposed = true;
}
```
- CA1063(IDisposable 패턴 위반)을 `pragma`로 무시하고 있는데, 정작 `Dispose(bool)`이 `private`이라 파생 클래스가 native handle을 정리할 길이 없다. 표준 패턴(`protected virtual void Dispose(bool)`)이 깨져 있다.
- `disposing` 분기가 비어 있는데 굳이 매개변수만 들고 있다.
- `_infoHandle`은 unmanaged이므로 `disposing == false`일 때(=finalizer 경로)에서만 정리해야 하는 게 아니라, 둘 다 정리되어야 하는데, 그 부분만은 맞다.

**영향**: 향후 파생 클래스에서 cleanup 확장 불가, code analyzer noise.
**권장**: `protected virtual void Dispose(bool disposing)`로 시그니처 변경, `pragma` 제거. `disposing == true`일 때 managed cleanup, unmanaged cleanup은 둘 다.

### 2.2 `_callbacks` 구조체가 backend 인스턴스에서만 유지 — backend GC 시 native callback 호출 시 invalid pointer
**파일**: `src/Tizen.Applications.Team/Tizen.Applications.CoreBackend/TeamUICoreBackend.cs:29` (및 Service/View 동일)
**문제**: `_callbacks` 구조체는 backend 인스턴스 필드로 보관되며, backend 인스턴스는 `TeamApplication._backend`가 strong ref로 들고 있다. 그러나 `TeamApplication` 자체가 GC되면(=사용자 코드가 ref를 놓치면) `_callbacks` 구조체도 사라지고, 그 안의 delegate들도 collect된다. 그런데 이 delegate들은 native 측에서 함수 포인터로 보관되어 호출되므로, GC 후 native 호출 시 NullReferenceException 또는 segfault가 발생한다.
다행히 `TeamManager.RegisterRunningTeamApp(this)`가 `_runningApps` static HashSet에 보관하여 strong ref를 잡아주지만, 이는 `TeamApplication.Run()`을 호출한 경우에만 등록된다. `Run()` 호출 전에 backend가 GC되는 시나리오는 드물지만, 코드상 명시적 보장은 없다.
**영향**: 방어 메커니즘이 우연에 의존. 향후 코드 변경 시 회복 불가능한 버그로 발현 가능.
**권장**: backend 자체가 `TeamManager`에 등록되도록 하거나 `GCHandle.Alloc(_callbacks, GCHandleType.Normal)`을 backend 생성자에서 할당, Dispose에서 Free하라.

### 2.3 `TeamApplicationInfo`가 사용하는 callback delegate가 lambda로 캡쳐되어 GC 위험
**파일**: `src/Tizen.Applications.Team/TeamApplicationInfo.cs:235-256`, `346-365`, `471-491`
**문제**: 각 property는 호출 시점에 lambda를 만들어 native에 전달한 후 끝나면 GC되도록 설계되어 있다. `GC.KeepAlive(cb)`가 호출 끝부분에 있어 호출 중에는 살아있긴 하지만, native 함수가 동기적으로 모든 callback을 호출한 다음 리턴한다고 가정한 경우에만 안전하다. 만약 native가 비동기로 콜백을 호출한다면(현재 시그니처상 동기로 보이지만 향후 변경 시) 즉시 dangling. 그리고 `GC.KeepAlive`는 컴파일러가 확실하게 ref를 유지해주는 마지막 사용 지점일 뿐, JIT의 escape 분석에 따라 더 일찍 collect될 수 있다.
**영향**: 일반적으로 안전하지만, 향후 native API 변경에 취약.
**권장**: 명시적으로 `Interop.ApplicationManager.AppInfoMetadataCallback cb = ...; var handle = GCHandle.Alloc(cb); try { ... } finally { handle.Free(); }` 패턴을 사용하거나, 동기 호출 보장에 대한 주석을 추가하라.

### 2.4 `TeamLoop.DoCreateLibPath`의 `Marshal.StringToHGlobalAnsi` 메모리 — 해제 책임 불명확
**파일**: `src/Tizen.Applications.Team/TeamLoop.cs:237`
**문제**:
```csharp
output = Marshal.StringToHGlobalAnsi(libPath);
```
이 native 메모리를 누가 해제하는지 코드 어디에도 명시되어 있지 않다. native 측에서 free()를 호출한다면 `Marshal.StringToHGlobalAnsi`가 사용한 `Marshal.AllocHGlobal`은 plain malloc과 호환되지 않을 수 있다(CRT 별로 다름).
**영향**: 메모리 누수 또는 해제 시 crash.
**권장**: 네이티브 측의 free 규약을 확인하고, 만약 native가 free()를 호출한다면 `Marshal.AllocHGlobal` 대신 `g_strdup(libPath)`(GLib alloc)을 통해 할당하거나 native측에서 `g_free`를 호출하도록 맞추어야 한다. 주석으로 해제 규약 명시 필수.

### 2.5 `GSourceManager`의 `_wrapperHandler` static delegate가 GC됨
**파일**: `src/Tizen.Applications.Team/GSourceManager.cs:29`
**문제**: `private static readonly Interop.Glib.GSourceFunc _wrapperHandler = new ...(Handler);`로 static 보관해서 GC 위험은 낮으나, `_wrapperHandler`는 native에 함수 포인터로 전달되고 native가 보관한다. static field는 영구적이므로 일반적으로 안전하지만, `AssemblyLoadContext.Unload()`(현재 모듈에서 그대로 사용)로 컨텍스트가 unload되면 static field도 사라진다. 만약 host process가 collectible ALC 안에서 이 모듈을 로드한 경우, ALC unload 후 native callback이 호출되면 NRE.
**영향**: 일반 시나리오에서는 안전. 그러나 dynamic load 환경에서 위험.
**권장**: `GCHandle.Alloc(_wrapperHandler, GCHandleType.Normal)`로 추가 pin하거나, host process에서만 사용된다는 가정을 명시하라.

### 2.6 `GSourceManager.Post`의 dead code — `context`가 항상 `IntPtr.Zero`
**파일**: `src/Tizen.Applications.Team/GSourceManager.cs:31-36`
**문제**:
```csharp
public static void Post(Action action)
{
    IntPtr context = IntPtr.Zero;
    var sourceContext = context == IntPtr.Zero ? _tizenContext : _tizenUIContext;
    sourceContext.Post(action, context, _wrapperHandler);
}
```
`context`는 `IntPtr.Zero`로만 초기화되며 변경된 적이 없으므로 `_tizenUIContext` 분기는 영원히 dead code. `_tizenUIContext` 자체가 사용되지 않는 dead field가 된다.
또한 `Handler` 메서드도 `userData == IntPtr.Zero` 분기가 항상 참이므로 마찬가지.
**영향**: 메모리 낭비 + 잘못된 모델 (UI context가 동작 안 함).
**권장**: UI context를 사용해야 한다면 `Post(Action action, bool useUIContext = false)` 식으로 시그니처 확장. 아니라면 `_tizenUIContext`와 분기 제거.

### 2.7 `TeamCoreBackend` 자체에 finalizer가 없고 native handle 보유 — leak 위험
**파일**: `src/Tizen.Applications.Team/Tizen.Applications.CoreBackend/TeamCoreBackend.cs:32-42`
**문제**: `_memberHandle`, `_loadObjId`, `_argHandle` 모두 unmanaged 핸들이지만 base class `TeamCoreBackend`에는 finalizer가 없고, 파생 클래스(`TeamUICoreBackend` 등)도 finalizer를 추가하지 않았다. 사용자가 명시적으로 Dispose하지 않으면 `team_member_quit`이 호출되지 않아 native 멤버가 leak된다.
**영향**: 멤버 핸들 누수.
**권장**: `TeamCoreBackend`에 finalizer 추가하거나, `DefaultCoreBackend`의 finalizer가 `Dispose(false)`를 호출하는지 확인하고 그 흐름에서 native 정리가 일어나도록 한다.

## 3. 코드 개선점

### 3.1 `TeamApplicationInfo`의 path getter 6개가 동일 패턴 → 헬퍼 추출 필요
**파일**: `src/Tizen.Applications.Team/TeamApplicationInfo.cs:374-556` 및 `TeamDirectoryInfo.cs` 전체
**문제**: `SharedDataPath`, `SharedResourcePath`, `SharedTrustedPath`, `ExternalSharedDataPath`, `CommonSharedDataPath`, `CommonSharedTrustedPath`가 모두
```csharp
if (_xxxPath == null) {
    var err = Interop.TeamManager.TeamAppGetXxxPath(_memberHandle, out string path);
    if (err != ...) Log.Warn(...);
    _xxxPath = path;
}
return _xxxPath;
```
패턴이고 `TeamDirectoryInfo`는 14개의 거의 동일한 property가 존재한다. 보일러플레이트가 250+ 라인이며 새 path 추가 시 6개 위치를 손봐야 한다.
**권장**:
```csharp
private string GetCachedPath(ref string cache, Func<IntPtr, (TeamAppErrorCode, string)> nativeGet, string name) { ... }
```
형태의 helper 추출. 또는 `Dictionary<string, string>` 캐시 + delegate 매핑.

### 3.2 `TeamApplicationInfo.err` 인스턴스 필드 — race-condition 가능성
**파일**: `src/Tizen.Applications.Team/TeamApplicationInfo.cs:41`
**문제**:
```csharp
private Interop.ApplicationManager.ErrorCode err = Interop.ApplicationManager.ErrorCode.None;
```
`err`이 인스턴스 필드인데 모든 property에서 공유한다. 동시 호출 시 마지막 호출이 `err`을 덮어써 다른 스레드의 결과 판정이 깨질 수 있고, 의미상 local variable이어야 한다. 게다가 이 인스턴스 필드 자체가 외부에 노출되지도 않아 의미가 없다.
**권장**: 각 property 안에서 `var err = ...`로 local 변수로 선언하라.

### 3.3 `TeamApplication.Current` 인스턴스 property는 의미 모호
**파일**: `src/Tizen.Applications.Team/TeamApplication.cs:131-132`
**문제**:
```csharp
public TeamApplication Current { get { return this; } }
```
`Current`라는 이름은 보통 `static` "현재 active한 어플리케이션" 의미인데, 인스턴스 메서드로서 항상 `this`를 반환하므로 사용 의미가 전혀 없다.
**권장**: 제거하거나, 진짜 `static TeamApplication Current`로 정의(현재 thread의 active TeamApplication을 반환)하라.

### 3.4 `TeamCoreUiApplication.OnCreate()` override가 `base.OnCreate()`만 호출 — 무용
**파일**: `src/Tizen.Applications.Team/TeamCoreUiApplication.cs:86-89`
**문제**: 1.2와 동일한 패턴. 빈 override는 제거하라.

### 3.5 `TeamUICoreBackend.SetDefaultWindowId`가 `Log.Error`로 정상 흐름을 로깅
**파일**: `src/Tizen.Applications.Team/Tizen.Applications.CoreBackend/TeamUICoreBackend.cs:83-87`
**문제**:
```csharp
internal void SetDefaultWindowId(int windowId)
{
    Log.Error(LogTag, $"SetDefaultWindowId: {windowId}");
    DefaultWindowId = windowId;
}
```
정상 흐름인데 `Log.Error`로 남기면 dlog 로그 모니터링 시 false alarm이 다수 발생한다.
**권장**: `Log.Info` 또는 `Log.Debug`로 수정.

### 3.6 `LogTag` 중복 정의 (`new static string`)
**파일**: `TeamUICoreBackend.cs:28`, `TeamServiceCoreBackend.cs:27`, `TeamViewCoreBackend.cs:29`
**문제**: `internal new static string LogTag = "DN_TAM";`로 base class의 이름을 가리는 패턴인데, `TeamCoreBackend`(base)에는 `LogTag`가 정의되어 있지 않다(`DefaultCoreBackend`에 있음). `new` 키워드가 의미 없거나 잘못된 사용일 가능성이 있다. 또한 `static` 필드가 `string`으로 정의되어 있어 외부 코드가 변경 가능하다(`internal`이긴 하지만).
**권장**: `private const string LogTag = "DN_TAM";`로 변경하고 `new` 키워드 제거. 모든 클래스에서 동일.

### 3.7 `SystemLocaleConverter.Exist`가 매번 `CultureInfo.GetCultures(AllCultures)`를 호출 — O(n) 매 locale 조회
**파일**: `src/Tizen.Applications.Team/SystemLocaleConverter.cs:144-154`
**문제**:
```csharp
private bool Exist(string locale)
{
    foreach (var cultureInfo in CultureInfo.GetCultures(CultureTypes.AllCultures))
        if (cultureInfo.Name == locale) return true;
    return false;
}
```
`CultureInfo.GetCultures(AllCultures)`는 수백 개의 culture를 매번 새로 enumerate한다. fallback 흐름에서 5번까지 호출될 수 있어 비싸다.
**권장**: static `HashSet<string>`으로 한 번만 캐싱.

### 3.8 `SystemLocaleConverter.ULocale.Locale`의 `string.IsNullOrEmpty(_locale)` → 매 getter마다 ICU 호출 가능
**파일**: `src/Tizen.Applications.Team/SystemLocaleConverter.cs:184-196`
**문제**: 캐싱 조건이 `string.IsNullOrEmpty(_locale)`이라 `Canonicalize`가 빈 문자열을 반환하면 매 호출마다 ICU 호출이 반복된다. 동일 패턴이 `Language`, `Script`, `Country`, `Variant`에도 있다.
**권장**: 빈 문자열도 "이미 시도했음"을 의미하도록 별도 bool 플래그(`_canonicalized`)를 두거나 `_locale = _locale ?? Canonicalize(...) ?? string.Empty;`로 표현하라. `_locale != null`로 캐시 유무 판정.

### 3.9 `TeamManager`의 ID counter들이 `int.MaxValue`까지 monotonic — overflow 미고려
**파일**: `src/Tizen.Applications.Team/TeamManager.cs:67, 71`
**문제**: `_assemblyId`, `_viewIdCounter`가 단조증가만 한다. 매우 장기 실행 시나리오에서 overflow 가능. 또한 `int`를 `IntPtr`로 캐스트하므로 host가 64비트일 때 32비트 영역만 사용.
**영향**: 실제로는 거의 발생하지 않지만, robustness 차원에서 남겨두기에는 부적절.
**권장**: overflow 시 wrap-around 후 충돌 검사, 혹은 `Guid` 기반 ID. 최소 `if (id == int.MaxValue) throw new InvalidOperationException(...)` 가드.

### 3.10 `TeamManager.AddView`가 `string.IsNullOrEmpty(appid)`에 대해 `ArgumentNullException`을 던짐 — 타입 부정확
**파일**: `src/Tizen.Applications.Team/TeamManager.cs:213, 264, 297`
**문제**: 빈 문자열은 null이 아니므로 `ArgumentException` 또는 `ArgumentException("appid is empty")`가 적절.
**권장**:
```csharp
if (appid == null) throw new ArgumentNullException(nameof(appid));
if (appid.Length == 0) throw new ArgumentException("appid is empty", nameof(appid));
```

## 4. 보안 / 견고성

### 4.1 `TeamLoop.DoCreateLibPath`가 path traversal 방어 없음
**파일**: `src/Tizen.Applications.Team/TeamLoop.cs:218-237`
**문제**:
```csharp
string memberId = path.Substring(lastDotIndex + 1);
string libPath = $"/usr/share/csteam/dll/{memberId}.dll";
```
`memberId`에 `../` 같은 path traversal 문자가 들어와도 검증 없이 path 합성에 사용. native가 어떤 제어를 하는지 모르지만, C# 측 input validation으로 보호하는 게 안전하다.
**권장**: `if (memberId.Any(c => Path.GetInvalidFileNameChars().Contains(c))) return error;` 추가.

### 4.2 `SystemLocaleConverter.ULocale.Canonicalize` 등이 `StringBuilder`를 ANSI 가정으로 사용
**파일**: `src/Tizen.Applications.Team/SystemLocaleConverter.cs:268-328`
**문제**: ICU가 반환하는 locale string은 ASCII만 담기므로 일반적으로는 안전하지만, `[MarshalAs(UnmanagedType.LPStr)]`/`[MarshalAs(UnmanagedType.LPUTF8Str)]`이 명시되지 않은 채 `StringBuilder`로 받는 것은 모호하다. P/Invoke 기본값은 플랫폼/캐릭터셋에 의존.
**권장**: Interop 선언에서 명시적으로 `CharSet = CharSet.Ansi` 또는 `Utf8`을 지정. `StringBuilder` capacity가 ICU의 실제 요구치보다 작으면 truncation 발생 가능 → ICU API에 따라 필요 크기를 받아 다시 호출하는 패턴 검토.

### 4.3 `CultureInfoHelper.GetCultureName`의 `_fileExists`가 static field로 한 번만 평가 — runtime에 파일이 추가되어도 무시
**파일**: `src/Tizen.Applications.Team/CultureInfoHelper.cs:29`
**문제**: `private static bool _fileExists = File.Exists(_pathCultureInfoIni);`는 type initialization 시점에 한 번만 평가. 부팅 초기에 .ini가 아직 없을 수도 있는데 그 경우 영원히 fallback. 또한 보안 측면에서, OTA 업데이트 후에도 재시작 전까지는 새 .ini를 못 읽는다.
**권장**: 매 호출 시 `File.Exists` 체크 (한 번 더 system call 정도는 무시할 수 있는 비용) 또는 lazy로 처음 호출 시 평가.

### 4.4 `Marshal.PtrToStringAnsi`가 null을 반환할 수 있음
**파일**: `src/Tizen.Applications.Team/SystemLocaleConverter.cs:345`, `src/Tizen.Applications.Team/CultureInfoHelper.cs:51`
**문제**: 두 곳 모두 `Marshal.PtrToStringAnsi(ptr)`의 null 반환을 검증하지 않는다. `GetDefaultLocale`이 null을 반환하면 caller인 `Convert(null)`이 NRE를 일으킨다 (`new ULocale(null)` 자체는 OK이지만 `Canonicalize(null)`은 ICU에서 실패).
**권장**: `?? string.Empty` 추가.

## 5. API 디자인 이슈

### 5.1 `Tizen.Applications.ResourceControl` 클래스가 `Common`과 `Team` 모듈에 동시 존재 → 컴파일 시 충돌
**파일**:
- `src/Tizen.Applications.Team/ResourceControl.cs`
- `src/Tizen.Applications.Common/Tizen.Applications/ResourceControl.cs`

**문제**: 두 파일 모두 `namespace Tizen.Applications`에 `public class ResourceControl`을 선언한다. `Tizen.Applications.Team.csproj`가 `Tizen.Applications.Common.csproj`를 `ProjectReference`로 가지므로, 두 동일한 fully qualified name `Tizen.Applications.ResourceControl`이 서로 다른 assembly에 존재하게 된다. 사용자가 Team 모듈을 참조하면 컴파일러는 CS0436 warning을 내고, `TeamApplicationInfo.ResourceControls`가 반환하는 `ResourceControl`이 사실 `Tizen.Applications.Team.dll`의 것이라 type mismatch가 발생한다.
**영향**: API 사용자가 `using Tizen.Applications;` 후 `ResourceControl`을 사용할 때 어느 쪽인지 불명확. `Common`을 참조한 다른 모듈과 type incompatibility.
**권장**: Team 모듈의 `ResourceControl`을 제거하고 `Tizen.Applications.Common`의 것을 그대로 사용. 추가 필드가 필요하다면 새 클래스 `Tizen.Applications.TeamResourceControl`로 분리.

### 5.2 `TeamCoreBackend._memberHandle` 등이 `protected` — 캡슐화 위반
**파일**: `src/Tizen.Applications.Team/Tizen.Applications.CoreBackend/TeamCoreBackend.cs:32-42`
**문제**: `protected IntPtr _memberHandle = IntPtr.Zero;`. 파생 클래스가 native 핸들에 직접 접근 가능. 또한 abstract property `MemberHandle`을 별도로 정의하고 있어 중복.
**권장**: 필드를 `private`로 만들고 `protected` setter를 가진 property로 노출.

### 5.3 `TeamUICoreBackend.GetDefaultWindow`가 `public`인데 backend 클래스 자체는 `internal`
**파일**: `src/Tizen.Applications.Team/Tizen.Applications.CoreBackend/TeamUICoreBackend.cs:88-91`
**문제**: 클래스가 `internal class TeamUICoreBackend`인데 메서드가 `public Window GetDefaultWindow()`. 외부에서 접근 불가능하므로 `public`은 의미 없다(엄밀히 말하면 public IL 노출은 일어나지만 type access modifier가 internal이라 사용 불가).
**권장**: `internal Window GetDefaultWindow()`로 통일.

### 5.4 `TeamApplication.Backend` property는 `protected`이지만 `_backend` 필드도 `protected readonly` → 둘 중 하나로 통일
**파일**: `src/Tizen.Applications.Team/TeamApplication.cs:44-49`
**문제**: 동일한 객체를 두 가지 경로(`_backend`, `Backend`)로 노출. 파생 클래스(`TeamCoreApplication.Run`)에서 `_backend.Run(args)`와 `Backend.AddEventHandler(...)`를 섞어 쓰고 있다.
**권장**: `_backend`를 `private readonly`로 만들고 `Backend` property만 노출. 또는 둘 다 `protected`로 두되 일관성 있게 `Backend`만 사용.

### 5.5 `TeamLoop.GetSystemArgs()`가 호출자에게 mutable array 노출
**파일**: `src/Tizen.Applications.Team/TeamLoop.cs:250-254`
**문제**: `_systemArgs`를 그대로 반환하므로 호출자가 배열을 수정할 수 있고, 그 변화가 다음 `DoCreateArgs`에 영향을 미친다. `DoCreateArgs`는 `(string[])GetSystemArgs().Clone()`으로 방어하고 있지만, 외부 사용자가 호출하는 경우는 보호되지 않는다.
**권장**: `(string[])_systemArgs?.Clone()`을 반환.

## 우선순위 요약

| 우선순위 | 이슈 | 파일:라인 |
|---------|------|----------|
| **High** | 1.3 `argsClone` 길이/복사 인덱스 오류 | `TeamUICoreBackend.cs:111-117`, `TeamServiceCoreBackend.cs:84-90`, `TeamViewCoreBackend.cs:111-117` |
| **High** | 1.5 `mainMethod.Name` NRE 가능성 | `TeamLoop.cs:179-186` |
| **High** | 1.7 `AssemblyLoadContext` strong ref 누락 → 조기 collect | `TeamLoop.cs:108-113` |
| **High** | 1.8 `Post<T>`의 throw로 인한 deadlock | `TeamCoreApplication.cs:312-314` |
| **High** | 2.1 `TeamApplicationInfo.Dispose(bool)` 비표준 패턴 | `TeamApplicationInfo.cs:584-599` |
| **High** | 2.2 backend `_callbacks` GC 위험 | `TeamUICoreBackend.cs:29` 외 |
| **High** | 2.7 `TeamCoreBackend` finalizer 부재 → native 핸들 leak | `TeamCoreBackend.cs:32-42` |
| **High** | 5.1 `ResourceControl` 네임스페이스 충돌 | `Tizen.Applications.Team/ResourceControl.cs` ↔ `Tizen.Applications.Common/.../ResourceControl.cs` |
| **Medium** | 1.4 finalizer에서 `Exit()` 미호출 | `TeamApplication.cs:223-238` |
| **Medium** | 1.6 `WeakReference.Target` race | `TeamLoop.cs:143-148` |
| **Medium** | 2.3 ApplicationInfo callback delegate GC 리스크 | `TeamApplicationInfo.cs:235-256` 등 |
| **Medium** | 2.4 `StringToHGlobalAnsi` 해제 책임 불명 | `TeamLoop.cs:237` |
| **Medium** | 2.6 `GSourceManager._tizenUIContext` dead code | `GSourceManager.cs:31-42` |
| **Medium** | 3.1 path getter 6개 보일러플레이트 | `TeamApplicationInfo.cs:374-556`, `TeamDirectoryInfo.cs` |
| **Medium** | 3.2 `err` 인스턴스 필드 race | `TeamApplicationInfo.cs:41` |
| **Medium** | 4.1 path traversal 방어 부재 | `TeamLoop.cs:218-237` |
| **Medium** | 4.4 `PtrToStringAnsi` null 미처리 | `SystemLocaleConverter.cs:345`, `CultureInfoHelper.cs:51` |
| **Low** | 1.1 OnCreate 윈도우 자동 표시 여부 문서화 | `TeamCoreApplication.cs:135-150` |
| **Low** | 1.2 `TeamUiApplication.Run` 무용 override | `TeamUiApplication.cs:45-48` |
| **Low** | 2.5 `GSourceFunc` static delegate ALC unload 시 위험 | `GSourceManager.cs:29` |
| **Low** | 3.3 `Current` 인스턴스 property | `TeamApplication.cs:131-132` |
| **Low** | 3.4 `TeamCoreUiApplication.OnCreate` 빈 override | `TeamCoreUiApplication.cs:86-89` |
| **Low** | 3.5 `SetDefaultWindowId`의 `Log.Error` | `TeamUICoreBackend.cs:83-87` |
| **Low** | 3.6 `LogTag`의 `new static string` 사용 | `TeamUICoreBackend.cs:28` 등 |
| **Low** | 3.7 `Exist`의 매번 GetCultures 호출 | `SystemLocaleConverter.cs:144-154` |
| **Low** | 3.8 `ULocale` cache 조건 | `SystemLocaleConverter.cs:184-196` |
| **Low** | 3.9 ID counter overflow | `TeamManager.cs:67, 71` |
| **Low** | 3.10 `ArgumentNullException` 타입 부정확 | `TeamManager.cs:213, 264, 297` |
| **Low** | 4.2 `StringBuilder` charset 명시 | `SystemLocaleConverter.cs:268-328` |
| **Low** | 4.3 `_fileExists` static 캐시 | `CultureInfoHelper.cs:29` |
| **Low** | 5.2 `_memberHandle` `protected` 노출 | `TeamCoreBackend.cs:32-42` |
| **Low** | 5.3 internal class 안의 public 메서드 | `TeamUICoreBackend.cs:88-91` |
| **Low** | 5.4 `_backend` / `Backend` 중복 노출 | `TeamApplication.cs:44-49` |
| **Low** | 5.5 `GetSystemArgs` mutable array 반환 | `TeamLoop.cs:250-254` |

전반적으로 모듈의 골격(추상 backend → 구체 backend 3종 → 추상 application → 구체 application 3종)은 깔끔하며, ACR 전 단계에서 모든 신규 API에 `[EditorBrowsable(EditorBrowsableState.Never)]`와 inhouse 주석이 일관되게 적용되어 있는 점은 칭찬할 만하다. 위 High 우선순위 이슈만 우선 처리해도 production 안정성이 크게 향상될 것이다.
