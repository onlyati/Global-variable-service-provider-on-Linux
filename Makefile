VERSION := "1.0.2"

build:
	dotnet build

clean:
	dotnet clean

rebuild:
	dotnet clean
	dotnet build

publish:
	dotnet publish

deploy:
	dotnet publish
	mkdir -p /usr/share/GlobalVariableProvider/${VERSION}/
	cp -r $(shell pwd)/bin/Debug/net5.0/publish/* /usr/share/GlobalVariableProvider/${VERSION}/
	chmod +x /usr/share/GlobalVariableProvider/${VERSION}/misc/globvar.sh
	rm -f /usr/share/GlobalVariableProvider/current
	ln -s /usr/share/GlobalVariableProvider/${VERSION} /usr/share/GlobalVariableProvider/current
	rm -f /bin/globvar
	ln -s /usr/share/GlobalVariableProvider/${VERSION}/misc/globvar.sh /bin/globvar

	