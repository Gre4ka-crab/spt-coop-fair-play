using System;
using System.Threading.Tasks;
using Fika.Core.Networking.Models.Headless;

namespace PrivateStack.FairPlay;

public static class SafeHeadlessStart
{
    public const string UncertainMessage = "Запуск не подтверждён. Headless мог принять запрос. Вернитесь в меню и проверьте его состояние; повторный запуск заблокирован.";
    public static async Task<StartHeadlessResponse> Normalize(Task<StartHeadlessResponse> task, string expectedId, Task timeout = null)
    {
        try
        {
            if (await Task.WhenAny(task, timeout ?? Task.Delay(30000)).ConfigureAwait(false) != task)
            {
                _ = Observe(task); return new StartHeadlessResponse { Error = UncertainMessage };
            }
            var result = await task.ConfigureAwait(false);
            if (!string.IsNullOrEmpty(result.Error)) return result;
            return result.MatchId == expectedId && !string.IsNullOrEmpty(expectedId) ? result : new StartHeadlessResponse { Error = UncertainMessage };
        }
        catch (Exception) { return new StartHeadlessResponse { Error = UncertainMessage }; }
    }
    static async Task Observe(Task task) { try { await task.ConfigureAwait(false); } catch { } }
}
