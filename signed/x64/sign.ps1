# signtool should be in C:\Program Files (x86)\Microsoft SDKs\ClickOnce\SignTool\

$CERT_NAME = "DWIVEDI, ALEXANDER ASHOK"

MakeCab /f rawaccel.ddf
signtool sign /n $CERT_NAME /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 disk1\rawaccel.cab
signtool sign /n $CERT_NAME /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 installer.exe
signtool sign /n $CERT_NAME /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 uninstaller.exe
