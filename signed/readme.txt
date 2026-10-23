To sign for ARCH
  - from <project root>\ARCH\Release\ to .\ARCH\, copy
    - installer.exe
    - uninstaller.exe
    - driver\
  - change CERT_NAME definition at the top of .\ARCH\sign.ps1
  - run .\ARCH\sign.ps1
  - submit .\ARCH\disk1\rawaccel.cab to MS Partner Center: https://partner.microsoft.com/en-us/dashboard/hardware/driver/New
  - extract rawaccel.sys from submission to .\ARCH\driver\ 