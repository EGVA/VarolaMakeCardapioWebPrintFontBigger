Start-Service -Name "RAWPrintProcessor"
Start-Process -FilePath "C:\Users\ericv\AppData\Local\zpl-escpos-printer\zpl-escpos-printer.exe"  -WindowStyle Hidden