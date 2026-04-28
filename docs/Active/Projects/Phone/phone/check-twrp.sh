#!/bin/bash
curl -sL 'https://eu.dl.twrp.me/hero2qltechn/' | grep -oP 'href="[^"]*\.img[^"]*"' | head -10
echo "---"
curl -sL 'https://eu.dl.twrp.me/hero2qltechn/' | grep -oP 'twrp-[0-9][^"<]+' | sort -u | head -20
