using GasStationSimulator;
using System.Timers;
using Timer = System.Timers.Timer;

internal class Program
{
    static int totalFuel = 2000;

    static bool isFuelDepleted = false;
    static bool isTimeoutReached = false;

    static SemaphoreSlim stationSemaphore = new(10);

    static object printLock = new();
    static object enterLock = new();
    static object fuelLock = new();

    static Timer stationTimer = new();
    static Timer spawnTimer = new();

    static Random random = new();
    static CancellationTokenSource shutdownTokenSource = new();

    public static void Main()
    {
        stationTimer.Interval = 5000;
        stationTimer.Elapsed += GasStationTimeoutTimerElapsed;
        stationTimer.AutoReset = false;
        stationTimer.Start();

        spawnTimer.Interval = random.Next(5, 25);
        spawnTimer.Elapsed += VehiclesCreationTimerElapsed;
        spawnTimer.Start();

       

        Console.ReadLine();
    }

    static void GasStationTimeoutTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        isTimeoutReached = true;
        PrintWithLock($"Gas station closing timeout of {stationTimer.Interval} elapsed");
        spawnTimer.Stop();
        shutdownTokenSource.Cancel();
    }

    static void FuelDepleted()
    {
        isFuelDepleted = true;
        PrintWithLock("Gas station closing: Fuel depleted");
        spawnTimer.Stop();
        stationTimer.Stop();
        shutdownTokenSource.Cancel();
    }

    static void VehicleProc(object? obj)
    {
        if (obj is not Vehicle vehicle) return;

        try
        {
            stationSemaphore.Wait(vehicle.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            PrintWithLock($"Vehicle {vehicle.Id} received cancellation request while waiting to enter the gas station");
            return;
        }

        lock (enterLock)
        {
            PrintWithLock($"Vehicle {vehicle.Id} entered the gas station");
            Thread.Sleep(50);
        }

        lock (fuelLock)
        {   
            if (totalFuel > 0)
            {
                
                int fuelRequested = random.Next(50, 150);
                if (fuelRequested > totalFuel)
                    fuelRequested = totalFuel;

                totalFuel -= fuelRequested;
              
                PrintWithLock($"Vehicle {vehicle.Id} fueled {fuelRequested}");
                PrintWithLock($"Total fuel: {totalFuel}");

                if (totalFuel == 0)
                    FuelDepleted();
                
            }
        }

        PrintWithLock($"Vehicle {vehicle.Id} leaved the gas station");
        
        stationSemaphore.Release();
    }

    static void VehiclesCreationTimerRearm(object? sender, ElapsedEventArgs e)
    {
         spawnTimer.Interval = random.Next(50, 250);
    }

    static void VehiclesCreationTimerElapsed(object? sender, ElapsedEventArgs e)
    {
 

        var cts = new CancellationTokenSource();
        if (isTimeoutReached)
        {
            cts.Cancel();
            return;
        }
        var vehicle = new Vehicle(cts.Token);
        PrintWithLock($"Vehicle {vehicle.Id} created");

        var t = new Thread(VehicleProc);
        t.Start(vehicle);

        VehiclesCreationTimerRearm(sender, e);
    }

    static void PrintWithLock(string str)
    {
        lock (printLock)
        {
            Console.WriteLine(str);
        }
    }
}
