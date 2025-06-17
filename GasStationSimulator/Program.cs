using GasStationSimulator;
using System.Threading;
using System.Timers;
using Timer = System.Timers.Timer;

internal class Program
{
    private static int totalFuel = 15000;
    private static SemaphoreSlim stationSemaphore = new(10, 10);

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
        // Initialize and start the station timeout timer
        stationTimer.Interval = 5000;
        stationTimer.Elapsed += GasStationTimeoutTimerElapsed;
        stationTimer.AutoReset = false;
        stationTimer.Start();

        // Start vehicle spawning timer
        spawnTimer.Elapsed += VehiclesCreationTimerElapsed;
        spawnTimer.Start();

        PrintWithLock($"total fuel: {totalFuel}");

        // Wait until the station closes and all vehicles have left
        while (!isTimeoutReached || stationSemaphore.CurrentCount < 10)
        {
            Thread.Sleep(100);
        }

        PrintWithLock($"Vehicles count: {vehicleCount}");

        // Release resources
        stationTimer?.Dispose();
        spawnTimer?.Dispose();
        stationSemaphore?.Dispose();

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    // Called when the station timeout timer elapses
    static void GasStationTimeoutTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        lock (fuelLock)
        {
            if (isFuelDepleted)
            {
                // Fuel already depleted; skip closing due to timeout
                return;
            }

            PrintWithLock($"Gas station closing timeout of {stationTimer.Interval} elapsed");
            isTimeoutReached = true;
            shutdownTokenSource.Cancel();
            spawnTimer.Stop();
        }
    }

    // Called when fuel reaches zero
    static void FuelDepleted()
    {
        lock (fuelLock)
        {
            if (isTimeoutReached && !isFuelDepleted)
            {
                // Timeout already occurred; now also depleting fuel
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

    // Execution logic for each vehicle thread
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
            bool enteredStation = false;

            try
            {
                // Attempt to enter the gas station (acquire semaphore)
                stationSemaphore.Wait(vehicle.CancellationToken);
                enteredStation = true;

                if (isTimeoutReached)
                {
                    PrintWithLock($"Vehicle {vehicle.Id} aborted: gas station closed");
                    return;
                }

                // Check if fuel is already depleted
                lock (fuelLock)
                {
                    if (isFuelDepleted)
                    {
                        PrintWithLock($"Vehicle {vehicle.Id} leaved the gas station");
                        return;
                    }
                }

                PrintWithLock($"Vehicle {vehicle.Id} entered the gas station");

                // Simulate time taken to refuel
                Thread.Sleep(random.Next(100, 500));

                lock (fuelLock)
                {
                    if (isFuelDepleted)
                    {
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

    // Sets a new random interval for vehicle creation
    static void VehiclesCreationTimerRearm(object? sender, ElapsedEventArgs e)
    {
        if (!isTimeoutReached)
        {
            spawnTimer.Interval = random.Next(5, 26);
        }
    }

    // Called when it's time to spawn a new vehicle
    static void VehiclesCreationTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (isTimeoutReached)
        {
            spawnTimer.Stop();
            return;
        }

        Vehicle vehicle = new Vehicle(shutdownTokenSource.Token);
        Interlocked.Increment(ref vehicleCount);

        PrintWithLock($"Vehicle {vehicle.Id} created");

        Thread vehicleThread = new Thread(VehicleProc)
        {
            IsBackground = true
        };
        vehicleThread.Start(vehicle);

        VehiclesCreationTimerRearm(null, null);
    }

    // Ensures thread-safe printing to console
    static void PrintWithLock(string str)
    {
        lock (printLock)
        {
            Console.WriteLine(str);
        }
    }
}
