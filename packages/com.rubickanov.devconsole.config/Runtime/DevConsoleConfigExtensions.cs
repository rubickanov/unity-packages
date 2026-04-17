using System;
using Rubickanov.Config;

namespace Rubickanov.DevConsole.Config
{
    /// <summary>
    /// Extension methods that wire a <see cref="ConfigDatabase{T}"/> into the dev console:
    /// argument parser by Id + autocomplete provider for the item type.
    /// </summary>
    public static class DevConsoleConfigExtensions
    {
        /// <summary>
        /// Registers <paramref name="db"/> as the source for parsing and autocompleting items of type <typeparamref name="T"/>.
        /// After this call, console commands declaring a <typeparamref name="T"/> parameter resolve it via <c>db.Get(input)</c>
        /// and get autocomplete over <c>db.All[i].Id</c>.
        /// </summary>
        public static CommandRegistry RegisterConfigDatabase<T>(
            this CommandRegistry registry, ConfigDatabase<T> db)
            where T : ConfigBase, IIdentifiable
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (db == null) throw new ArgumentNullException(nameof(db));

            registry.RegisterParser<T>(input =>
            {
                var item = db.Get(input);
                return item != null ? (true, item) : (false, default);
            });
            registry.RegisterDefaultProvider<T>(new ConfigDatabaseAutoCompleteProvider<T>(db));
            return registry;
        }

        public static CommandRegistry RegisterConfigDatabases<T1>(
            this CommandRegistry registry, ConfigDatabase<T1> db1)
            where T1 : ConfigBase, IIdentifiable
            => registry.RegisterConfigDatabase(db1);

        public static CommandRegistry RegisterConfigDatabases<T1, T2>(
            this CommandRegistry registry, ConfigDatabase<T1> db1, ConfigDatabase<T2> db2)
            where T1 : ConfigBase, IIdentifiable
            where T2 : ConfigBase, IIdentifiable
            => registry.RegisterConfigDatabase(db1).RegisterConfigDatabase(db2);

        public static CommandRegistry RegisterConfigDatabases<T1, T2, T3>(
            this CommandRegistry registry,
            ConfigDatabase<T1> db1, ConfigDatabase<T2> db2, ConfigDatabase<T3> db3)
            where T1 : ConfigBase, IIdentifiable
            where T2 : ConfigBase, IIdentifiable
            where T3 : ConfigBase, IIdentifiable
            => registry.RegisterConfigDatabase(db1)
                       .RegisterConfigDatabase(db2)
                       .RegisterConfigDatabase(db3);

        public static CommandRegistry RegisterConfigDatabases<T1, T2, T3, T4>(
            this CommandRegistry registry,
            ConfigDatabase<T1> db1, ConfigDatabase<T2> db2,
            ConfigDatabase<T3> db3, ConfigDatabase<T4> db4)
            where T1 : ConfigBase, IIdentifiable
            where T2 : ConfigBase, IIdentifiable
            where T3 : ConfigBase, IIdentifiable
            where T4 : ConfigBase, IIdentifiable
            => registry.RegisterConfigDatabase(db1)
                       .RegisterConfigDatabase(db2)
                       .RegisterConfigDatabase(db3)
                       .RegisterConfigDatabase(db4);

        public static CommandRegistry RegisterConfigDatabases<T1, T2, T3, T4, T5>(
            this CommandRegistry registry,
            ConfigDatabase<T1> db1, ConfigDatabase<T2> db2,
            ConfigDatabase<T3> db3, ConfigDatabase<T4> db4,
            ConfigDatabase<T5> db5)
            where T1 : ConfigBase, IIdentifiable
            where T2 : ConfigBase, IIdentifiable
            where T3 : ConfigBase, IIdentifiable
            where T4 : ConfigBase, IIdentifiable
            where T5 : ConfigBase, IIdentifiable
            => registry.RegisterConfigDatabase(db1)
                       .RegisterConfigDatabase(db2)
                       .RegisterConfigDatabase(db3)
                       .RegisterConfigDatabase(db4)
                       .RegisterConfigDatabase(db5);
    }
}
