# Global Variable Provider

I prefer not to hardcode parameters and input data onto my monitoring scripts. Besides a long parameter list can look ugly. So I decided to make a program onto Linux user space, which has only one task: holding information in its memory and manipulate it as requests coming from outside of its address space. To store memory, I used this nuge package: [OnlyAti.MemoryDbLibrary package](https://www.nuget.org/packages/OnlyAti.MemoryDbLibrary/) | [Repository](https://github.com/onlyati/Simple-in-memory-dotnet-database). This project was intended to run on Linux operating system.

## How does it work?

My purpose was made it to simply as I can. Thus I did not use sockets for communication but rather use named pipes in Linux operating system.

Application using this sequence during it's running:
1. Program initialize ThreadPool
2. Program initialize MemoryDB from Nuget library
3. Program creates the receiver named pipe
4. It is waiting for the request
   - If request was come, it sends the request the a background thread and waiting for the next request
   - Background track execute what needed then send back output if the request

Application working represented on a figure:
```
+---------------+                                            +-------------+                                     +------------+
| Shell script  | --- Invoke script with desired action ---> | globvar.sh  | --- Forward with proper header ---> | Application|
+---------------+                                            +-------------+                                     +------------+
           ^                                                    |      ^                                                 |
           |                                                    |      |                                                 |
           +-------- Return with response-----------------------+      +-------------------------------------------------+
```

## Possible actions

Actions can be call with provided globvar shell script in this format:
```
globvar <action>
```

Action can be:
```
Read record:         get <key>
List sub records:    getdir <key>
List all records:    getall
Create record:       set <key> <value>
Delete record:       set <key>
Delete all records:  delall
Delete sub records:  deldir <key>
Save record:         save <key>
Load record:         load <key> <override: true/false>
Load all records:    loadall <override: true/false
Delete from file:    purge <key>
```

## Installation steps

#### Pull project from repo and build

In a work directory execute it:
```
git init
git remote add origin https://github.com/onlyati/Global-variable-service-provider-on-Linux.git
git pull origin master   
```

If it has been pulled down, the publish with dotnet (or using makefile):
```
dotnet build
dotnet publish
```
Note: .NET 5.0 runtime environment is required to run this. Check Microsoft webpage about how to install it to your distro.

### Using Makefile
By using Makefile it is easier to deploy:
```
make build
make publish
sudo make deploy
```

Create directory and copy product to `/usr/share/GlobalVariableProvider/${VERSION}`.
It also create a symlink for globvar.sh as globvar onto /bin directory.

#### Shell script for interface

Create file called "globvar" (without double quotes) in /usr/bin directory. It can be found: misc/globvar.sh

By this command, request can be send to provider. Some sample:
```
globvar set something/test/val1 Here is the value
globvar get something/test/val1
```

To get a summary about possible commands, just type `globvar` without any parameter.

#### Create new systemd service for more uptime

Create new member in systemd library, for example on Debian 10: /lib/systemd/system/GlobalVariableProvider.service
Content of this file is here:
```
[Unit]
Description=Global variable service provider

[Service]
WorkingDirectory=/usr/share/GlobalVariableProvider/current
User=root
ExecStart=/usr/bin/dotnet GlobalVariableProvider.dll
Restart=on-failure
RestartSec=30

[Install]
WantedBy=multi-user.target
```

After systemd reload it is possible to start and stop the service:
```
systemctl daemon-reload
systemctl start GlobalVariableProvider
systemctl stop GlobalVariableProvider
systemctl restart GlobalVariableProvider
```

Log also writes:
```
journalctl -u GlobalVariableProvider
```

