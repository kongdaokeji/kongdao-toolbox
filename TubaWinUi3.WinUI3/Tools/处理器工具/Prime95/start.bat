@echo off
echo 烤鸡说明：
echo 通过Prime95给CPU尽可能增加负载，以测试CPU的稳定性和CPU散热系统的散热能力是否达标。
echo 烤鸡有危险，图吧工具箱官方不为使用本工具产生的任何后果负责！
echo 这个测试比AIDA64的单烤FPU负载更高。
echo 提示：测试期间可以使用Core Temp监控CPU的负载、温度、电压、功率。
echo 提示：考虑到部分小白可能不知道怎么退出Prime95烤鸡，所以本工具也支持一键结束Prime95程序。
echo 请确保CPU散热和电源供电没有问题，然后按下任意键开始烤鸡。
pause >nul
Start prime95.exe -t
cls
echo 已开始烤鸡，在此期间您可以打开图吧工具箱-CPU工具里边的Core Temp监控CPU的负载、温度、电压、功率。
echo 提示：当前为单烤CPU模式，如需双烤，可以打开图吧工具箱-显卡工具里边的Furmark一键烤鸡。
echo 再次提示：请确保CPU散热和电源供电没有问题。
echo 按下任意键可以停止烤鸡并退出。
pause >nul
taskkill /f /im prime95.exe
exit