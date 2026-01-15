using SystemProfilerCli;

var app = new CommandApp<ProfileCommand>();

return app.Run(args);

// TODO: See if the top 3 processes' names in the live table can be left aligned (just the names-the rest of the column should stay centred as well as the other columns).
