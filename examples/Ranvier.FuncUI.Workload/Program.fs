module Workload.Program

open Avalonia
open Avalonia.Headless
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Avalonia.Threading
open System

type App() =
    inherit Application()

    override this.Initialize() =
        this.Styles.Add (FluentTheme ())

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            let args = Environment.GetCommandLineArgs ()

            let variant =
                args
                |> Array.tryFind (fun arg -> Array.contains arg Runners.names)
                |> Option.defaultValue "ranvier"

            let size =
                args
                |> Array.tryFindIndex ((=) "--items")
                |> Option.map (fun i -> Int32.Parse args[i + 1])
                |> Option.defaultValue 1000

            let runner = Runners.create variant (Model.init size) false

            let window =
                Window (Content = runner.Host, Width = 1100., Height = 800., Title = sprintf "Task workload — %s" variant)

            let trace = Workloads.trace "background" size
            let mutable index = 0
            let timer = DispatcherTimer (Interval = TimeSpan.FromMilliseconds 100.)

            timer.Tick.Add (fun _ ->
                runner.Dispatch trace[index % trace.Length]
                index <- index + 1)

            window.Closed.Add (fun _ ->
                timer.Stop ()
                runner.Dispose ())

            desktop.MainWindow <- window
            timer.Start ()

            if Array.contains "--smoke" args then
                let shutdown = DispatcherTimer (Interval = TimeSpan.FromSeconds 2.)

                shutdown.Tick.Add (fun _ ->
                    shutdown.Stop ()
                    desktop.Shutdown ())

                shutdown.Start ()
        | _ -> ()

        base.OnFrameworkInitializationCompleted ()

[<EntryPoint>]
let main args =
    if
        Array.contains "--verify" args
        || Array.contains "--measure" args
    then
        AppBuilder.Configure<App>().UseHeadless(AvaloniaHeadlessPlatformOptions ()).SetupWithoutStarting()
        |> ignore

        if Array.contains "--verify" args then
            Verification.run ()

        if Array.contains "--measure" args then
            let output =
                args
                |> Array.tryFindIndex ((=) "--output")
                |> Option.map (fun i -> args[i + 1])
                |> Option.defaultValue "results/raw.json"

            Measurement.run output

        0
    else
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args)
