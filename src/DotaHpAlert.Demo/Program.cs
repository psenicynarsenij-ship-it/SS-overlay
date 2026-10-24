using DotaHpAlert.Core;

var clock = new DemoClock();
var engine = new AlertEngine(new AlertSettings { InvokerEnabled = true }, clock);
int[] hp = [800, 500, 349, 320, 320, 400];
int[] seconds = [0, 1, 2, 3, 7, 8];
for (var i = 0; i < hp.Length; i++)
{
    clock.SetSeconds(seconds[i]);
    var decision = engine.Evaluate(new(hp[i], clock.GetUtcNow(), 1, true, true));
    Console.WriteLine($"t={seconds[i]}s HP={hp[i]} State={decision.State} Action={decision.Action} Reason={decision.Reason}");
}

sealed class DemoClock : TimeProvider
{
    private long seconds;
    public override long TimestampFrequency => 1;
    public override long GetTimestamp() => seconds;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(seconds);
    public void SetSeconds(long value) => seconds = value;
}
