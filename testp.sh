#!/usr/bin/sh

mkfifo globvar-$$ -m666
echo -n "globvar-$$ $*" > globvar-in
respond=$(cat globvar-$$)
rm globvar-$$

echo "${respond}"

exit 0

