// Los tests de integración de este ensamblado pegan todos contra el mismo
// PeachEBills local (.\SQLEXPRESS). Corriéndolos en paralelo entre sí —y con los
// otros proyectos de test— se satura el pool de conexiones y algún SkippableFact
// falla de forma intermitente. Serializarlos dentro del ensamblado lo evita.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
