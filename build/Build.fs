﻿// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

open System
open System.IO
open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators


//-----------------------------------------------------------------------------
// Metadata and Configuration
//-----------------------------------------------------------------------------

let rootDirectory = __SOURCE_DIRECTORY__ </> ".."

let sln = rootDirectory </> "FSharp.Data.sln"


let (!!) includes =
    (!!includes).SetBaseDirectory rootDirectory

let project = "FSharp.Data"
let authors = "Tomas Petricek;Gustavo Guerra;Colin Bull;fsprojects contributors"
let summary = "Library of F# type providers and data access tools"

let description =
    """
  The FSharp.Data packages contain type providers and utilities to access
  common data formats (CSV, HTML, JSON and XML in your F# applications and scripts.

  * FSharp.Data -- includes everything
    * FSharp.Data.Http -- http types/helpers
    * FSharp.Data.Csv.Core -- csv types/helpers
    * FSharp.Data.Json.Core -- json types/helpers
    * FSharp.Data.Html.Core -- html types/helpers
    * FSharp.Data.Xml.Core -- xml types/helpers"""

let tags =
    "F# fsharp data typeprovider WorldBank CSV HTML CSS JSON XML HTTP linqpad-samples"

let gitOwner = "fsprojects"
let gitHome = "https://github.com/" + gitOwner
let gitName = "FSharp.Data"

let packageProjectUrl = "https://fsprojects.github.io/FSharp.Data/"
let repositoryType = "git"
let repositoryUrl = "https://github.com/fsprojects/FSharp.Data"
let license = "Apache-2.0"

// Read release notes & version info from RELEASE_NOTES.md
let release = ReleaseNotes.load "/Users/andreas/Projects/public/FSharp.Data/RELEASE_NOTES.md"

let isCI = Environment.GetEnvironmentVariable("CI") <> null


/// So we don't require always being on the latest MSBuild.StructuredLogger
let disableBinLog (p: MSBuild.CliArguments) = { p with DisableInternalBinLog = true }

//-----------------------------------------------------------------------------
// Target Implementations
//-----------------------------------------------------------------------------



let initTargets () =
    // --------------------------------------------------------------------------------------
    // Generate assembly info files with the right version & up-to-date information

    Target.create "AssemblyInfo" (fun _ ->
        for file in !! "src/AssemblyInfo*.fs" do
            let replace (oldValue: string) newValue (str: string) = str.Replace(oldValue, newValue)

            let title =
                Path.GetFileNameWithoutExtension file
                |> replace "AssemblyInfo" "FSharp.Data"

            let versionSuffix = ".0"
            let version = release.AssemblyVersion + versionSuffix

            AssemblyInfoFile.createFSharp
                file
                [ AssemblyInfo.Title title
                  AssemblyInfo.Product project
                  AssemblyInfo.Description summary
                  AssemblyInfo.Version version
                  AssemblyInfo.FileVersion version ])

    // --------------------------------------------------------------------------------------
    // Clean build results

    Target.create "Clean" (fun _ ->
        // remove all bin/obj folders (except from the project we are running right now)
        !! "src/**/bin"
        ++ "src/**/obj"
        ++ "tests/**/bin"
        ++ "tests/**/obj"
        |> Shell.cleanDirs)

    Target.create "CleanDocs" (fun _ -> Shell.cleanDirs [ "docs/output" ])

    let internetCacheFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.InternetCache)

    Target.create "CleanInternetCaches" (fun _ ->
        Shell.cleanDirs
            [ internetCacheFolder @@ "DesignTimeURIs"
              internetCacheFolder @@ "WorldBankSchema"
              internetCacheFolder @@ "WorldBankRuntime" ])

    // --------------------------------------------------------------------------------------
    // Build library & test projects

    Target.create "Build" (fun _ ->
        sln
        |> DotNet.build (fun o -> {
            o with
                Configuration = DotNet.BuildConfiguration.Release
                MSBuildParams = disableBinLog o.MSBuildParams
        }))

    Target.create "RunTests" (fun _ ->
        let setParams (o: DotNet.TestOptions) =
            { o with
                Configuration = DotNet.BuildConfiguration.Release
                MSBuildParams = disableBinLog o.MSBuildParams
                Logger =
                    if isCI then
                        Some "GitHubActions"
                    else
                        None }

        sln |> DotNet.test setParams)

    // --------------------------------------------------------------------------------------
    // Build packages

    Target.create "Pack" (fun _ ->
        // Format the release notes
        let releaseNotes = release.Notes |> String.concat "\n"

        let properties =
            [ ("Version", release.NugetVersion)
              ("Authors", authors)
              ("PackageProjectUrl", packageProjectUrl)
              ("PackageTags", tags)
              ("RepositoryType", repositoryType)
              ("RepositoryUrl", repositoryUrl)
              ("PackageLicenseExpression", license)
              ("PackageReleaseNotes", releaseNotes)
              ("Summary", summary)
              ("PackageDescription", description) ]

        DotNet.pack
            (fun p ->
                { p with
                    Configuration = DotNet.BuildConfiguration.Release
                    OutputPath = Some "bin"
                    MSBuildParams = disableBinLog { p.MSBuildParams with Properties = properties } })
            sln)

    // --------------------------------------------------------------------------------------
    // Generate the documentation
    Target.create "GenerateDocs" (fun _ ->
        Shell.cleanDir ".fsdocs"

        let result =
            DotNet.exec
                id
                "fsdocs"
                ("build --properties Configuration=Release --strict --eval --clean --parameters fsdocs-package-version "
                 + release.NugetVersion)

        if not result.OK then
            printfn "Errors while generating docs: %A" result.Messages
            failwith "Failed to generate docs")

    // --------------------------------------------------------------------------------------
    // Help

    Target.create "Help" (fun _ ->
        printfn ""
        printfn "  Please specify the target by calling 'build -t <Target>'"
        printfn ""
        printfn "  Targets for building:"
        printfn "  * Build"
        printfn "  * RunTests"
        printfn "  * GenerateDocs"
        printfn "  * Pack (creates package only, doesn't publish)"
        printfn "  * All (calls previous 4)"
        printfn ""
        printfn "  Other targets:"
        printfn "  * CleanInternetCaches"
        printfn "  * Format"
        printfn "  * CheckFormat"
        printfn "")

    let sourceFiles =
        !! "src/**/*.fs" ++ "src/**/*.fsi" ++ "build.fsx"
        -- "src/**/obj/**/*.fs"
        -- "src/AssemblyInfo*.fs"

    Target.create "Format" (fun _ ->
        let result =
            sourceFiles
            |> Seq.map (sprintf "\"%s\"")
            |> String.concat " "
            |> DotNet.exec id "fantomas"

        if not result.OK then
            printfn "Errors while formatting all files: %A" result.Messages)

    Target.create "CheckFormat" (fun _ ->
        let result =
            sourceFiles
            |> Seq.map (sprintf "\"%s\"")
            |> String.concat " "
            |> sprintf "%s --check"
            |> DotNet.exec id "fantomas"

        if result.ExitCode = 0 then
            Trace.log "No files need formatting"
        elif result.ExitCode = 99 then
            failwith "Some files need formatting, run `dotnet fake build -t Format` to format them"
        else
            Trace.logf "Errors while formatting: %A" result.Errors
            failwith "Unknown errors while formatting")

    Target.create "All" ignore

    "Clean"
    ==> "AssemblyInfo"
    ==> "CheckFormat"
    ==> "Build"
    |> ignore

    "Build"
    ==> "CleanDocs"
    ==> "GenerateDocs"
    ==> "All"
    |> ignore

    "Build" ==> "Pack" ==> "All" |> ignore
    "Build" ==> "All" |> ignore
    "Build" ==> "RunTests" ==> "All" |> ignore


//-----------------------------------------------------------------------------
// Target Start
//-----------------------------------------------------------------------------
[<EntryPoint>]
let main argv =
    argv
    |> Array.toList
    |> Context.FakeExecutionContext.Create false "build.fsx"
    |> Context.RuntimeContext.Fake
    |> Context.setExecutionContext

    initTargets ()
    Target.runOrDefaultWithArguments ("Build")

    0 // return an integer exit code