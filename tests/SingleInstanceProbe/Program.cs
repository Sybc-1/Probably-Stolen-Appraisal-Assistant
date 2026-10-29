using PawnAppraiser.Desktop;

using var instance = new SingleInstance();
Console.WriteLine(instance.IsOwner ? "OWNER" : "SECONDARY");
if (instance.IsOwner)
{
    instance.Listen(() => Console.WriteLine("SHOW"));
    Console.ReadLine();
}
