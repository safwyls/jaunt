dotnet run --project ./CakeBuild/CakeBuild.csproj -- "$@"
rm -r Equus/bin/
rm -r Equus/obj/
rm "${VINTAGE_STORY_DEV}"/Mods/equus_*.zip
cp Releases/equus_*.zip "${VINTAGE_STORY_DEV}/Mods"
