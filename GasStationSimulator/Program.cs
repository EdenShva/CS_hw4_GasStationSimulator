using GasStationSimulator;
using System.Threading;
using System.Timers;
using Timer = System.Timers.Timer;

internal class Program
{
    private static int totalFuel = 15000;
    private static SemaphoreSlim stationSemaphore = new(10,10);

    private static bool isFuelDepleted = false;
    private static bool isTimeoutReached = false;

    private static Timer stationTimer = new();
    private static Timer spawnTimer = new();

    private static object printLock = new object();

    private static object fuelLock = new object();

    private static Random random = new Random();
    private static CancellationTokenSource shutdownTokenSource = new CancellationTokenSource();

    private static int vehicleCount = 0;

    public static void Main()
    {
        stationTimer.Interval = 5000;
        stationTimer.Elapsed += GasStationTimeoutTimerElapsed;
        stationTimer.AutoReset = false;
        stationTimer.Start();

        spawnTimer.Elapsed += VehiclesCreationTimerElapsed;
        spawnTimer.Start();

        PrintWithLock($"total fuel: {totalFuel}");

        // Wait for station to close
        while (!isTimeoutReached || stationSemaphore.CurrentCount < 10)
        {
            Thread.Sleep(100);
        }

        PrintWithLock($"Vehicles count: {vehicleCount}");

        // Clean up
        stationTimer?.Dispose();
        spawnTimer?.Dispose();
        stationSemaphore?.Dispose();

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    static void GasStationTimeoutTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        lock (fuelLock)
        {
            if (isFuelDepleted)
            {
                // If fuel already depleted, ignore timeout
                return;
            }

            PrintWithLock($"Gas station closing timeout of {stationTimer.Interval} elapsed");
            isTimeoutReached = true;
            shutdownTokenSource.Cancel();
            spawnTimer.Stop();
        }
    }

    static void FuelDepleted()
    {
        lock (fuelLock)
        {
            if (isTimeoutReached && !isFuelDepleted)
            {
                // Timeout happened first, but now fuel is depleted
                PrintWithLock("Gas station closing: Fuel depleted");
                isFuelDepleted = true;
                shutdownTokenSource.Cancel();
                return;
            }

            if (!isFuelDepleted)
            {
                PrintWithLock("Gas station closing: Fuel depleted");
                isFuelDepleted = true;
                isTimeoutReached = true;
                shutdownTokenSource.Cancel();
                spawnTimer.Stop();
                stationTimer.Stop();
            }
        }
    }

    static void VehicleProc(object? obj)
    {
        Vehicle vehicle = (Vehicle)obj;

        if (isTimeoutReached)
        {
            PrintWithLock($"Vehicle {vehicle.Id} aborted: gas station closed");
            return;
        }

        try
        {
            // Try to enter the gas station
            bool enteredStation = false;

            try
            {
                // Wait for semaphore with cancellation token
                stationSemaphore.Wait(vehicle.CancellationToken);
                enteredStation = true;

                if (isTimeoutReached)
                {
                    PrintWithLock($"Vehicle {vehicle.Id} aborted: gas station closed");
                    return;
                }

                // Check if station is closed due to fuel depletion
                lock (fuelLock)
                {
                    if (isFuelDepleted)
                    {
                        PrintWithLock($"Vehicle {vehicle.Id} leaved the gas station");
                        return;
                    }
                }

                PrintWithLock($"Vehicle {vehicle.Id} entered the gas station");

                // Simulate fueling time
                Thread.Sleep(random.Next(100, 500));

                // Try to fuel
                lock (fuelLock)
                {
                    if (isFuelDepleted)
                    {
                        // Fuel depleted while waiting, just leave
                        PrintWithLock($"Vehicle {vehicle.Id} leaved the gas station");
                        return;
                    }

                    if (totalFuel > 0)
                    {
                        int fuelAmount = random.Next(1, Math.Min(200, totalFuel + 1));
                        totalFuel -= fuelAmount;

                        PrintWithLock($"Vehicle {vehicle.Id} fueled {fuelAmount}");
                        PrintWithLock($"Total fuel: {totalFuel}");

                        if (totalFuel <= 0)
                        {
                            totalFuel = 0;
                            FuelDepleted();
                        }
                    }
                }

                PrintWithLock($"Vehicle {vehicle.Id} leaved the gas station");
            }
            catch (OperationCanceledException)
            {
                if (!enteredStation)
                {
                    PrintWithLock($"Vehicle {vehicle.Id} received cancellation request while waiting to enter the gas station");
                }
                else
                {
                    // If already in station, still need to leave
                    PrintWithLock($"Vehicle {vehicle.Id} leaved the gas station");
                }
            }
            finally
            {
                if (enteredStation)
                {
                    stationSemaphore.Release();
                }
            }
        }
        catch (Exception ex)
        {
            PrintWithLock($"Vehicle {vehicle.Id} error: {ex.Message}");
        }
    }

    static void VehiclesCreationTimerRearm(object? sender, ElapsedEventArgs e)
    {
        if (!isTimeoutReached)
        {
            spawnTimer.Interval = random.Next(5, 26); // 5-25 milliseconds
        }
    }

        static void VehiclesCreationTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (isTimeoutReached)
        {
            spawnTimer.Stop();
            return;
        }

        // Create new vehicle
        Vehicle vehicle = new Vehicle(shutdownTokenSource.Token);
        Interlocked.Increment(ref vehicleCount);

        PrintWithLock($"Vehicle {vehicle.Id} created");

        // Start vehicle thread
        Thread vehicleThread = new Thread(VehicleProc);
        vehicleThread.IsBackground = true;
        vehicleThread.Start(vehicle);

        // Rearm timer for next vehicle
        VehiclesCreationTimerRearm(null, null);
    }

    static void PrintWithLock(string str)
    {
        lock (printLock)
        {
            Console.WriteLine(str);
        }
    }
}
