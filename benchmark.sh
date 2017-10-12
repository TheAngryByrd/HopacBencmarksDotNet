export FrameworkPathOverride=$(dirname $(which mono))/../lib/mono/4.5/

kill $(lsof -ti :5432)
rm -rf bin/ obj/ BenchmarkDotNet.Artifacts/ data/

dotnet restore
dotnet build -c Release 

# initdb -D data/
# sed -i .conf -e"s/^max_connections = 100.*$/max_connections = 1000/" data/postgresql.conf
# sed -i .conf -e"s/^shared_buffers =.*$/shared_buffers = 16GB/" data/postgresql.conf
# sed -i .conf -e"s/^#effective_cache_size = 128MB.*$/effective_cache_size = 48GB/" data/postgresql.conf
# sed -i .conf -e"s/^#work_mem = 1MB.*$/work_mem = 16MB/" data/postgresql.conf
# sed -i .conf -e"s/^#maintenance_work_mem = 16MB.*$/maintenance_work_mem = 2GB/" data/postgresql.conf
# sed -i .conf -e"s/^#checkpoint_segments = .*$/checkpoint_segments = 32/" data/postgresql.conf
# sed -i .conf -e"s/^#checkpoint_completion_target = 0.5.*$/checkpoint_completion_target = 0.7/" data/postgresql.conf
# sed -i .conf -e"s/^#wal_buffers =.*$/wal_buffers = 16MB/" data/postgresql.conf
# sed -i .conf -e"s/^#default_statistics_target = 100.*$/default_statistics_target = 100/" data/postgresql.conf


# postgres -D data &

mono --arch=64 bin/Release/net46/asyncToTask.exe

pkill -P $$

kill $
