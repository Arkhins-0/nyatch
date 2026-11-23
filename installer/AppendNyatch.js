// After a folder is picked with Browse: Nyatch goes in its own "Nyatch" folder inside it.
var chosen = Session.Property("INSTALLFOLDER");
if (chosen && !/\Nyatch\?$/i.test(chosen)) {
    Session.Property("INSTALLFOLDER") = chosen.replace(/\?$/, "\\") + "Nyatch\\";
}
