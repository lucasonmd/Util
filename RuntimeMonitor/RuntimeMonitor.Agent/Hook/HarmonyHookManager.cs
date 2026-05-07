using HarmonyLib;
using RuntimeMonitor.Agent.Network;
using RuntimeMonitor.Core.DTOs;
using System.Collections.Concurrent;
using System.Reflection;

namespace RuntimeMonitor.Agent.Hook;

/// <summary>
/// Harmony 기반 메서드 패치 관리자
/// 설계 원칙:
///   - Hook(Prefix) 내부에서는 최소 작업만 수행 (큐에 넣기만)
///   - 직렬화/변환은 백그라운드 스레드에서 수행
///   - 예외가 절대 대상 프로그램에 전파되지 않도록 보호
/// </summary>
public sealed class HarmonyHookManager : IDisposable
{
    private static readonly string HarmonyId = "com.runtimemonitor.agent";
    private readonly Harmony _harmony;
    private readonly PacketSendQueue _sendQueue;

    // static 필드로 Patch 메서드에서 접근 (Harmony prefix는 static이어야 함)
    private static PacketSendQueue? _staticQueue;

    // 메서드 키 → 파라미터 정보 캐시 (Reflection 최소화)
    private static readonly ConcurrentDictionary<string, PatchContext> _patchContexts = new();

    // 타입명 캐시 (GetType().FullName 반복 호출 방지)
    internal static readonly ConcurrentDictionary<Type, string> TypeNameCache = new();

    public HarmonyHookManager(PacketSendQueue sendQueue)
    {
        _harmony = new Harmony(HarmonyId);
        _sendQueue = sendQueue;
        _staticQueue = sendQueue;
    }

    /// <summary>
    /// 지정 타입의 메서드에 Prefix 패치 적용
    /// parameterIndex: 캡처할 파라미터의 인덱스 (-1이면 모든 파라미터 캡처)
    /// </summary>
    public void Patch<TTarget>(string methodName, int parameterIndex = 0)
    {
        PatchMethod(typeof(TTarget), methodName, parameterIndex);
    }

    /// <summary>
    /// 타입 객체로 직접 패치 - 런타임에 타입을 알 수 없을 때 사용
    /// </summary>
    public void PatchMethod(Type targetType, string methodName, int parameterIndex = 0)
    {
        var method = targetType.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static);

        if (method is null)
            throw new InvalidOperationException(
                $"메서드를 찾을 수 없음: '{targetType.FullName}.{methodName}'");

        var parameters = method.GetParameters();
        var paramName = parameterIndex >= 0 && parameterIndex < parameters.Length
            ? (parameters[parameterIndex].Name ?? $"arg{parameterIndex}")
            : "all";

        var contextKey = BuildContextKey(targetType, methodName);
        _patchContexts[contextKey] = new PatchContext(
            contextKey, parameterIndex, paramName, parameters.Length);

        var prefix = new HarmonyMethod(
            typeof(HarmonyHookManager).GetMethod(
                nameof(UniversalPrefix),
                BindingFlags.Static | BindingFlags.NonPublic)!);

        _harmony.Patch(method, prefix: prefix);
    }

    /// <summary>
    /// 모든 Hook에서 공유하는 Prefix 메서드
    ///
    /// 성능 최적화 포인트:
    ///   1. TryWrite는 non-blocking - Hook 스레드를 절대 블록하지 않음
    ///   2. 무거운 작업(직렬화)은 큐 워커 스레드에서 처리
    ///   3. try/catch로 모든 예외를 흡수 - 대상 프로그램 보호
    ///   4. _staticQueue null 체크로 초기화 전 호출 방어
    /// </summary>
    [HarmonyPrefix]
    private static void UniversalPrefix(object?[] __args, MethodBase __originalMethod)
    {
        // 예외가 대상 프로그램으로 전파되지 않도록 최상위 try/catch
        try
        {
            if (_staticQueue is null) return;
            if (__args is null || __args.Length == 0) return;

            var declaringType = __originalMethod.DeclaringType;
            if (declaringType is null) return;

            var contextKey = BuildContextKey(declaringType, __originalMethod.Name);
            if (!_patchContexts.TryGetValue(contextKey, out var ctx)) return;

            if (ctx.ParameterIndex >= 0)
            {
                // 단일 파라미터 캡처
                if (ctx.ParameterIndex >= __args.Length) return;
                var arg = __args[ctx.ParameterIndex];
                if (arg is null) return;

                // Hook 내에서 최소 작업 - object 참조만 전달
                _staticQueue.EnqueueRaw(
                    contextKey, arg,
                    ctx.ParameterIndex, ctx.ParameterName,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Environment.CurrentManagedThreadId);
            }
            else
            {
                // 모든 파라미터 캡처
                for (var i = 0; i < __args.Length; i++)
                {
                    if (__args[i] is null) continue;
                    _staticQueue.EnqueueRaw(
                        contextKey, __args[i]!,
                        i, $"arg{i}",
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Environment.CurrentManagedThreadId);
                }
            }
        }
        catch
        {
            // 모든 예외 무시 - 모니터링이 대상 프로그램에 영향을 주면 안 됨
        }
    }

    private static string BuildContextKey(Type type, string method)
        => $"{type.FullName}.{method}";

    public void Dispose()
    {
        _harmony.UnpatchAll(HarmonyId);
    }

    private sealed record PatchContext(
        string MethodKey,
        int ParameterIndex,
        string ParameterName,
        int TotalParameters);
}
