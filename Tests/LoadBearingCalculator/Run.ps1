$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$calculator = Join-Path $root 'Source/Revit.IFC.Export/Exporter/PropertySet/Calculators/LoadBearingCalculator.cs'
Add-Type -Path @((Join-Path $PSScriptRoot 'Regression.cs'), $calculator)
[LoadBearingRegression]::Run()
