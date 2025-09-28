# Intention: VINTAGE_STORY points to your Vintage Story install location
# while VINTAGE_STORY_DATA points to your Vintage Story data location
# Depending on how you set up those environment variables, you could use
# a different game install between playing and devving, or just a different
# data folder location (using the --dataPath argument on startup)

dotnet run --project ./CakeBuild/CakeBuild.csproj -- "$@"
rm -r Jaunt/bin/
rm -r Jaunt/obj/

VS_DATA=${VINTAGE_STORY_DATA:-~/.config/VintagestoryData}
rm "${VS_DATA}"/Mods/jaunt_*.zip
cp Releases/jaunt_*.zip "${VS_DATA}/Mods"
