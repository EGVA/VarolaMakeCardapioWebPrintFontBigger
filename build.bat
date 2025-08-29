dotnet publish -c Release -r win-x64

# Stop service
sc stop RAWPrintProcessor

# Check status
sc query RAWPrintProcessor

# Uninstall service
sc delete RAWPrintProcessor

sc create "RAWPrintProcessor" binPath="C:\Users\ericv\OneDrive\Documentos\Arruma Impressora Dotnet\bin\Release\net9.0\win-x64\publish\Arruma Impressora Dotnet.exe" start=auto


sc start RAWPrintProcessor

