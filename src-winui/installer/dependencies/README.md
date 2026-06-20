# Inno Setup Dependency Installer (optional, not active)

`CodeDependencies.iss` is vendored from [DomGries/InnoDependencyInstaller](https://github.com/DomGries/InnoDependencyInstaller) for optional future use. **GSBT Setup does not include or call it today** — release builds are self-contained and do not prompt for .NET at install time.

## Updating

Replace `CodeDependencies.iss` from upstream if you wire it back into `GSBT_Setup.iss`:

```powershell
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/DomGries/InnoDependencyInstaller/master/CodeDependencies.iss" -OutFile "CodeDependencies.iss"
```

## License

See upstream repository; distributed as third-party installer helper code, not linked into the GSBT application binary.
