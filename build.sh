dotnet run --project ./CakeBuild/CakeBuild.csproj -- "$@"
rm -r Jaunt/bin/
rm -r Jaunt/obj/
rm "${VINTAGE_STORY}"/Mods/jaunt_*.zip
cp Releases/jaunt_*.zip "${VINTAGE_STORY}/Mods"
