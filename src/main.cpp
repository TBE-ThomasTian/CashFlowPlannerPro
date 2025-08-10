#include "App.h"
#include <QApplication>
#include <QIcon>
#include <QFile>
#include <QStringList>
#include <QDebug>

int main(int argc, char *argv[]) {
    QApplication app(argc, argv);
    
    // Set application metadata
    app.setApplicationName("CashflowPlanner");
    app.setOrganizationName("CashflowPlanner");
    app.setApplicationDisplayName("Cashflow Planer");
    
    app.setDesktopFileName("CashflowPlanner.desktop");
    app.setWindowIcon(QIcon(":/images/resources/CashFlowIcon.ico"));
    
    App w;
    w.show();
    return app.exec();
}
