using SystemProfilerCli;

var app = new CommandApp<ProfileCommand>();

return app.Run(args);

// TODO: Add user's name as an option to be displayed in the summary details.
// TODO: See if the top 3 processes' names in the live table can be left aligned (just the names-the rest of the column should stay centred as well as the other columns).
// TODO: Have the console be cleared when process runs.
// TODO: Clean up log file format.
