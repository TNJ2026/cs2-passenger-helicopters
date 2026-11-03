param([string]$Editor='C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe',[switch]$Preview)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$project=Join-Path $PSScriptRoot 'rotor-shader-project'
$output=Join-Path $root 'artifacts\rotor-shader-build'
$log=Join-Path $root 'artifacts\rotor-shader-build.log'
$previous=$env:HELICOPTER_SHADER_OUTPUT
try {
 foreach($name in @('HelicopterRotorExposureRules','HelicopterRotorExposureMesh')){
  Copy-Item -LiteralPath (Join-Path $root ('Runtime\Models\'+$name+'.cs')) -Destination (Join-Path $project ('Assets\Editor\StagedRotor'+$name+'.cs')) -Force
 }
 $env:HELICOPTER_SHADER_OUTPUT=$output
 $arguments=@('-batchmode','-quit','-projectPath',('"'+$project+'"'),'-buildTarget','Win64','-executeMethod',$(if($Preview){'BuildRotorShader.BuildAndPreview'}else{'BuildRotorShader.Build'}),'-logFile',('"'+$log+'"'))
 if($Preview){$arguments+='-force-d3d11'}else{$arguments+='-nographics'}
 $process=Start-Process -FilePath $Editor -ArgumentList $arguments -WindowStyle Hidden -PassThru
 $process.WaitForExit()
 if($process.ExitCode -ne 0){throw ('Unity shader build failed. See '+$log)}
 if(!(Select-String -LiteralPath $log -SimpleMatch 'ROTOR_SHADER_BUILD_OK')){throw ('Unity shader build has no success marker. See '+$log)}
 if($Preview){foreach($marker in @('ROTOR_NATIVE_FULL_LAYOUT_OK','ROTOR_NATIVE_WELD_OK','ROTOR_GPU_PREFLIGHT_OK')){if(!(Select-String -LiteralPath $log -SimpleMatch $marker)){throw ('Missing native-layout preview verification: '+$marker)}}}
 $bundle=Join-Path $output 'rotor-angular-windows'
 $target=Join-Path $root 'assets\rendering\rotor-angular-windows.bundle'
 New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
 Copy-Item -LiteralPath $bundle -Destination $target -Force
 $inputs=@('tools/rotor-shader-project/Assets/RotorAngular.shader','Runtime/Models/HelicopterRotorExposureMesh.cs','Runtime/Models/HelicopterRotorExposureRules.cs')
 $inputs+=@('tools/rotor-shader-project/Assets/Editor/RotorNativeImportFixture.cs','tools/rotor-shader-project/Assets/Editor/RotorShaderChecks.cs','assets/native/ImportedData/CityWeaver_4a7fc21e03ce4e449bc02ae964c6ffe2/CWMilMi26tcPart01_9409d9f859de09b961b3ccac52bfc92d.Geometry')
 foreach($part in 1..6){$inputs+=('assets_game_import/mi26-rig-v6-small/CWMilMi26tcPart'+$part.ToString('D2')+'.obj')}
 $records=@($inputs | ForEach-Object {[pscustomobject]@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $root $_) -Algorithm SHA256).Hash.ToLowerInvariant()}})
 [pscustomobject]@{unity='2022.3.62f2';target='StandaloneWindows64';native_skin_check=$true;native_full_layout_verified=[bool]$Preview;gpu_preflight=[bool]$Preview;bundle_load_verified=[bool]$Preview;inputs=$records;sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'assets\rendering\rotor-angular-build.json') -Encoding utf8
 Get-FileHash -LiteralPath $target -Algorithm SHA256
} finally {$env:HELICOPTER_SHADER_OUTPUT=$previous}
