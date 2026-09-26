using static SmokeSupport;

internal static partial class SmokeScenarios
{
    private static async Task RunWorkspaceRuntimeAsync(SmokeWorkspaceContext context)
    {
        var byName = context.ByName;
        var root = context.Root;
        var file = context.File;
        var dotnetProjectRoot = context.DotnetProjectRoot;
        var dotnetProjectFile = context.DotnetProjectFile;
        var dotnetProgramFile = context.DotnetProgramFile;
        var dotnetOutputDll = context.DotnetOutputDll;
        var pythonVenvRoot = context.PythonVenvRoot;
        var chocoInfoResult = await EnsureSuccess(byName["talvora_choco_info"], new());
                if (chocoInfoResult.StructuredContent is not { } chocoInfoJson ||
                    string.IsNullOrWhiteSpace(chocoInfoJson.GetProperty("executable").GetString()) ||
                    string.IsNullOrWhiteSpace(chocoInfoJson.GetProperty("version").GetString()))
                {
                    throw new InvalidOperationException("choco_info did not return executable/version metadata.");
                }
                
                var chocoVersion = chocoInfoJson.GetProperty("version").GetString()!;
                
                var chocoListResult = await EnsureSuccess(byName["talvora_choco_list"], new()
                {
                    ["timeoutSeconds"] = 120,
                });
                if (chocoListResult.StructuredContent is not { } chocoListJson ||
                    chocoListJson.GetProperty("exitCode").GetInt32() != 0 ||
                    chocoListJson.GetProperty("timedOut").GetBoolean())
                {
                    throw new InvalidOperationException("choco_list did not complete successfully.");
                }
                
                var chocoRunResult = await EnsureSuccess(byName["talvora_choco_run"], new()
                {
                    ["arguments"] = new[] { "--version" },
                    ["timeoutSeconds"] = 30,
                });
                if (chocoRunResult.StructuredContent is not { } chocoRunJson ||
                    chocoRunJson.GetProperty("exitCode").GetInt32() != 0 ||
                    chocoRunJson.GetProperty("timedOut").GetBoolean() ||
                    !string.Equals(
                        (chocoRunJson.GetProperty("standardOutput").GetString() ?? string.Empty).Trim(),
                        chocoVersion,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("choco_run --version did not match choco_info.");
                }
                
                var dotnetInfoResult = await EnsureSuccess(byName["talvora_dotnet_info"], new());
                if (dotnetInfoResult.StructuredContent is not { } dotnetInfoJson ||
                    !dotnetInfoJson.GetProperty("found").GetBoolean() ||
                    string.IsNullOrWhiteSpace(dotnetInfoJson.GetProperty("executable").GetString()) ||
                    string.IsNullOrWhiteSpace(dotnetInfoJson.GetProperty("version").GetString()) ||
                    dotnetInfoJson.GetProperty("sdks").GetArrayLength() < 1)
                {
                    throw new InvalidOperationException("dotnet_info did not report the installed .NET SDK.");
                }
                
                var dotnetVersion = dotnetInfoJson.GetProperty("version").GetString()!;
                
                var dotnetRunResult = await EnsureSuccess(byName["talvora_dotnet_run"], new()
                {
                    ["workingDirectory"] = root,
                    ["arguments"] = new[] { "--version" },
                    ["timeoutSeconds"] = 30,
                });
                if (dotnetRunResult.StructuredContent is not { } dotnetRunJson ||
                    dotnetRunJson.GetProperty("exitCode").GetInt32() != 0 ||
                    dotnetRunJson.GetProperty("timedOut").GetBoolean() ||
                    !string.Equals(
                        (dotnetRunJson.GetProperty("standardOutput").GetString() ?? string.Empty).Trim(),
                        dotnetVersion,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("dotnet_run --version did not match dotnet_info.");
                }
                
                await EnsureSuccess(byName["talvora_create_directory"], new()
                {
                    ["path"] = dotnetProjectRoot,
                });
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = dotnetProgramFile,
                    ["content"] = "Console.WriteLine(\"TALVORA_DOTNET_SMOKE\");",
                });
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = dotnetProjectFile,
                    ["content"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>",
                });
                
                var dotnetRestoreResult = await EnsureSuccess(byName["talvora_dotnet_restore"], new()
                {
                    ["workingDirectory"] = dotnetProjectRoot,
                    ["target"] = dotnetProjectFile,
                    ["timeoutSeconds"] = 120,
                });
                if (dotnetRestoreResult.StructuredContent is not { } dotnetRestoreJson ||
                    dotnetRestoreJson.GetProperty("exitCode").GetInt32() != 0 ||
                    dotnetRestoreJson.GetProperty("timedOut").GetBoolean())
                {
                    throw new InvalidOperationException("dotnet_restore failed for the smoke project.");
                }
                
                var dotnetBuildResult = await EnsureSuccess(byName["talvora_dotnet_build"], new()
                {
                    ["workingDirectory"] = dotnetProjectRoot,
                    ["target"] = dotnetProjectFile,
                    ["configuration"] = "Release",
                    ["noRestore"] = true,
                    ["additionalArguments"] = new[] { "--nologo" },
                    ["timeoutSeconds"] = 120,
                });
                if (dotnetBuildResult.StructuredContent is not { } dotnetBuildJson ||
                    dotnetBuildJson.GetProperty("exitCode").GetInt32() != 0 ||
                    dotnetBuildJson.GetProperty("timedOut").GetBoolean())
                {
                    throw new InvalidOperationException("dotnet_build failed for the smoke project.");
                }
                
                var dotnetOutputInfo = await EnsureSuccess(byName["talvora_path_info"], new()
                {
                    ["path"] = dotnetOutputDll,
                });
                if (dotnetOutputInfo.StructuredContent is not { } dotnetOutputJson ||
                    !dotnetOutputJson.GetProperty("exists").GetBoolean() ||
                    !string.Equals(dotnetOutputJson.GetProperty("kind").GetString(), "file", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("dotnet_build did not create the expected output assembly.");
                }
                
                var dotnetExecutable = dotnetInfoJson.GetProperty("executable").GetString()
                    ?? throw new InvalidOperationException("dotnet_info executable was empty.");
                
                var peInfoResult = await EnsureSuccess(byName["talvora_pe_info"], new()
                {
                    ["path"] = dotnetExecutable,
                });
                if (peInfoResult.StructuredContent is not { } peInfoJson ||
                    !peInfoJson.GetProperty("isPe").GetBoolean() ||
                    peInfoJson.GetProperty("length").GetInt64() < 1 ||
                    string.IsNullOrWhiteSpace(peInfoJson.GetProperty("machine").GetString()))
                {
                    throw new InvalidOperationException("pe_info did not identify dotnet.exe as a PE image.");
                }
                
                var fileVersionResult = await EnsureSuccess(byName["talvora_file_version_info"], new()
                {
                    ["path"] = dotnetExecutable,
                });
                if (fileVersionResult.StructuredContent is not { } fileVersionJson ||
                    !string.Equals(
                        Path.GetFullPath(fileVersionJson.GetProperty("path").GetString() ?? string.Empty),
                        Path.GetFullPath(dotnetExecutable),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("file_version_info returned an unexpected file path.");
                }
                
                var msbuildInfoResult = await EnsureSuccess(byName["talvora_msbuild_info"], new());
                if (msbuildInfoResult.StructuredContent is not { } msbuildInfoJson ||
                    !msbuildInfoJson.GetProperty("found").GetBoolean() ||
                    string.IsNullOrWhiteSpace(msbuildInfoJson.GetProperty("executable").GetString()) ||
                    string.IsNullOrWhiteSpace(msbuildInfoJson.GetProperty("version").GetString()))
                {
                    throw new InvalidOperationException("msbuild_info did not resolve MSBuild or dotnet msbuild.");
                }
                
                var msbuildRunResult = await EnsureSuccess(byName["talvora_msbuild_run"], new()
                {
                    ["workingDirectory"] = root,
                    ["arguments"] = new[] { "-version", "-nologo" },
                    ["timeoutSeconds"] = 60,
                });
                if (msbuildRunResult.StructuredContent is not { } msbuildRunJson ||
                    msbuildRunJson.GetProperty("exitCode").GetInt32() != 0 ||
                    msbuildRunJson.GetProperty("timedOut").GetBoolean() ||
                    string.IsNullOrWhiteSpace(msbuildRunJson.GetProperty("standardOutput").GetString()))
                {
                    throw new InvalidOperationException("msbuild_run -version failed.");
                }
                
                var visualStudioResult = await EnsureSuccess(byName["talvora_visual_studio_instances"], new());
                if (visualStudioResult.StructuredContent is not { } visualStudioJson)
                {
                    throw new InvalidOperationException("visual_studio_instances did not return structured content.");
                }
                
                var visualStudioCount = visualStudioJson.GetProperty("count").GetInt32();
                
                var toolchainInfoResult = await EnsureSuccess(byName["talvora_windows_toolchain_info"], new());
                if (toolchainInfoResult.StructuredContent is not { } toolchainInfoJson ||
                    !toolchainInfoJson.TryGetProperty("msbuild", out var aggregateMsbuild) ||
                    !aggregateMsbuild.GetProperty("found").GetBoolean())
                {
                    throw new InvalidOperationException("windows_toolchain_info did not report the resolved MSBuild toolchain.");
                }
                
                if (visualStudioCount > 0)
                {
                    var usableInstance = visualStudioJson
                        .GetProperty("instances")
                        .EnumerateArray()
                        .FirstOrDefault(instance =>
                            instance.GetProperty("isComplete").GetBoolean() &&
                            instance.GetProperty("isLaunchable").GetBoolean() &&
                            !string.IsNullOrWhiteSpace(instance.GetProperty("installationPath").GetString()));
                
                    if (usableInstance.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        var installationPath = usableInstance.GetProperty("installationPath").GetString()!;
                        var devEnvironmentResult = await EnsureSuccess(byName["talvora_vs_dev_environment"], new()
                        {
                            ["installationPath"] = installationPath,
                            ["architecture"] = "x64",
                            ["hostArchitecture"] = "x64",
                            ["timeoutSeconds"] = 120,
                        });
                
                        if (devEnvironmentResult.StructuredContent is not { } devEnvironmentJson ||
                            devEnvironmentJson.GetProperty("count").GetInt32() < 1 ||
                            string.IsNullOrWhiteSpace(devEnvironmentJson.GetProperty("scriptPath").GetString()))
                        {
                            throw new InvalidOperationException("vs_dev_environment did not return a Visual Studio developer environment.");
                        }
                    }
                }
                
                var windowsSdkResult = await EnsureSuccess(byName["talvora_windows_sdk_info"], new());
                if (windowsSdkResult.StructuredContent is not { } windowsSdkJson)
                {
                    throw new InvalidOperationException("windows_sdk_info did not return structured content.");
                }
                if (windowsSdkJson.GetProperty("found").GetBoolean() &&
                    string.IsNullOrWhiteSpace(windowsSdkJson.GetProperty("kitsRoot10").GetString()))
                {
                    throw new InvalidOperationException("windows_sdk_info reported an SDK without KitsRoot10.");
                }
                
                
                var cmakeInfoResult = await EnsureSuccess(byName["talvora_cmake_info"], new());
                if (cmakeInfoResult.StructuredContent is not { } cmakeInfoJson)
                {
                    throw new InvalidOperationException("cmake_info did not return structured content.");
                }
                if (cmakeInfoJson.GetProperty("found").GetBoolean())
                {
                    var cmakeRunResult = await EnsureSuccess(byName["talvora_cmake_run"], new()
                    {
                        ["workingDirectory"] = root,
                        ["arguments"] = new[] { "--version" },
                        ["timeoutSeconds"] = 30,
                    });
                    if (cmakeRunResult.StructuredContent is not { } cmakeRunJson ||
                        cmakeRunJson.GetProperty("exitCode").GetInt32() != 0 ||
                        cmakeRunJson.GetProperty("timedOut").GetBoolean())
                    {
                        throw new InvalidOperationException("cmake_run --version failed.");
                    }
                }
                
                var ninjaInfoResult = await EnsureSuccess(byName["talvora_ninja_info"], new());
                if (ninjaInfoResult.StructuredContent is not { } ninjaInfoJson)
                {
                    throw new InvalidOperationException("ninja_info did not return structured content.");
                }
                if (ninjaInfoJson.GetProperty("found").GetBoolean())
                {
                    var ninjaRunResult = await EnsureSuccess(byName["talvora_ninja_run"], new()
                    {
                        ["workingDirectory"] = root,
                        ["arguments"] = new[] { "--version" },
                        ["timeoutSeconds"] = 30,
                    });
                    if (ninjaRunResult.StructuredContent is not { } ninjaRunJson ||
                        ninjaRunJson.GetProperty("exitCode").GetInt32() != 0 ||
                        ninjaRunJson.GetProperty("timedOut").GetBoolean())
                    {
                        throw new InvalidOperationException("ninja_run --version failed.");
                    }
                }
                
                var nodeInfoResult = await EnsureSuccess(byName["talvora_node_info"], new());
                if (nodeInfoResult.StructuredContent is not { } nodeInfoJson)
                {
                    throw new InvalidOperationException("node_info did not return structured content.");
                }
                
                var nodeFound = nodeInfoJson.GetProperty("nodeFound").GetBoolean();
                var npmFound = nodeInfoJson.GetProperty("npmFound").GetBoolean();
                
                if (nodeFound && string.IsNullOrWhiteSpace(nodeInfoJson.GetProperty("nodeVersion").GetString()))
                {
                    throw new InvalidOperationException("node_info reported Node.js without a version.");
                }
                
                if (npmFound)
                {
                    var npmVersion = nodeInfoJson.GetProperty("npmVersion").GetString();
                    if (string.IsNullOrWhiteSpace(npmVersion))
                    {
                        throw new InvalidOperationException("node_info reported npm without a version.");
                    }
                
                    var npmRunResult = await EnsureSuccess(byName["talvora_npm_run"], new()
                    {
                        ["workingDirectory"] = root,
                        ["arguments"] = new[] { "--version" },
                        ["timeoutSeconds"] = 30,
                    });
                    if (npmRunResult.StructuredContent is not { } npmRunJson ||
                        npmRunJson.GetProperty("exitCode").GetInt32() != 0 ||
                        npmRunJson.GetProperty("timedOut").GetBoolean() ||
                        !string.Equals(
                            (npmRunJson.GetProperty("standardOutput").GetString() ?? string.Empty).Trim(),
                            npmVersion,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("npm_run --version did not match node_info.");
                    }
                }
                
                var pythonInfoResult = await EnsureSuccess(byName["talvora_python_info"], new());
                if (pythonInfoResult.StructuredContent is not { } pythonInfoJson ||
                    !pythonInfoJson.GetProperty("found").GetBoolean() ||
                    string.IsNullOrWhiteSpace(pythonInfoJson.GetProperty("interpreterExecutable").GetString()) ||
                    string.IsNullOrWhiteSpace(pythonInfoJson.GetProperty("version").GetString()))
                {
                    throw new InvalidOperationException("python_info did not report the installed Python runtime.");
                }
                
                var pythonExecutable = pythonInfoJson.GetProperty("interpreterExecutable").GetString()!;
                
                var pythonRunResult = await EnsureSuccess(byName["talvora_python_run"], new()
                {
                    ["pythonExecutable"] = pythonExecutable,
                    ["workingDirectory"] = root,
                    ["arguments"] = new[] { "-c", "print('TALVORA_PYTHON_SMOKE')" },
                    ["timeoutSeconds"] = 30,
                });
                if (pythonRunResult.StructuredContent is not { } pythonRunJson ||
                    pythonRunJson.GetProperty("exitCode").GetInt32() != 0 ||
                    pythonRunJson.GetProperty("timedOut").GetBoolean() ||
                    !(pythonRunJson.GetProperty("standardOutput").GetString() ?? string.Empty)
                        .Contains("TALVORA_PYTHON_SMOKE", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("python_run did not execute the smoke expression.");
                }
                
                if (pythonInfoJson.GetProperty("pipAvailable").GetBoolean())
                {
                    var pipRunResult = await EnsureSuccess(byName["talvora_pip_run"], new()
                    {
                        ["pythonExecutable"] = pythonExecutable,
                        ["workingDirectory"] = root,
                        ["arguments"] = new[] { "--version" },
                        ["timeoutSeconds"] = 30,
                    });
                    if (pipRunResult.StructuredContent is not { } pipRunJson ||
                        pipRunJson.GetProperty("exitCode").GetInt32() != 0 ||
                        pipRunJson.GetProperty("timedOut").GetBoolean() ||
                        !(pipRunJson.GetProperty("standardOutput").GetString() ?? string.Empty)
                            .Contains("pip", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("pip_run --version failed.");
                    }
                }
                
                var venvCreateResult = await EnsureSuccess(byName["talvora_python_venv_create"], new()
                {
                    ["path"] = pythonVenvRoot,
                    ["pythonExecutable"] = pythonExecutable,
                    ["workingDirectory"] = root,
                    ["withoutPip"] = true,
                    ["timeoutSeconds"] = 120,
                });
                if (venvCreateResult.StructuredContent is not { } venvCreateJson ||
                    venvCreateJson.GetProperty("command").GetProperty("exitCode").GetInt32() != 0 ||
                    venvCreateJson.GetProperty("command").GetProperty("timedOut").GetBoolean() ||
                    string.IsNullOrWhiteSpace(venvCreateJson.GetProperty("pythonExecutable").GetString()))
                {
                    throw new InvalidOperationException("python_venv_create did not create a usable virtual environment.");
                }
                
                var venvPythonExecutable = venvCreateJson.GetProperty("pythonExecutable").GetString()!;
                var venvInfoResult = await EnsureSuccess(byName["talvora_python_info"], new()
                {
                    ["pythonExecutable"] = venvPythonExecutable,
                    ["workingDirectory"] = root,
                });
                if (venvInfoResult.StructuredContent is not { } venvInfoJson ||
                    !venvInfoJson.GetProperty("found").GetBoolean() ||
                    !venvInfoJson.GetProperty("inVirtualEnvironment").GetBoolean())
                {
                    throw new InvalidOperationException("python_info did not identify the created virtual environment.");
                }
                
                var dockerInfoResult = await EnsureSuccess(byName["talvora_docker_info"], new()
                {
                    ["workingDirectory"] = root,
                });
                if (dockerInfoResult.StructuredContent is not { } dockerInfoJson)
                {
                    throw new InvalidOperationException("docker_info did not return structured content.");
                }
                
                if (dockerInfoJson.GetProperty("found").GetBoolean())
                {
                    var dockerExecutable = dockerInfoJson.GetProperty("executable").GetString();
                    if (string.IsNullOrWhiteSpace(dockerExecutable))
                    {
                        throw new InvalidOperationException("docker_info reported Docker without an executable.");
                    }
                
                    var dockerRunResult = await EnsureSuccess(byName["talvora_docker_run"], new()
                    {
                        ["dockerExecutable"] = dockerExecutable,
                        ["workingDirectory"] = root,
                        ["arguments"] = new[] { "--version" },
                        ["timeoutSeconds"] = 30,
                    });
                    if (dockerRunResult.StructuredContent is not { } dockerRunJson ||
                        dockerRunJson.GetProperty("exitCode").GetInt32() != 0 ||
                        dockerRunJson.GetProperty("timedOut").GetBoolean() ||
                        !(dockerRunJson.GetProperty("standardOutput").GetString() ?? string.Empty)
                            .Contains("Docker", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("docker_run --version failed.");
                    }
                }
    }
}
