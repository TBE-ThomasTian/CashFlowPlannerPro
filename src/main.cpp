#include "App.h"
#include <QApplication>
#include <QIcon>

int main(int argc, char *argv[]) {
    QApplication app(argc, argv);
    
    // Set application icon - this sets it for all windows
    QIcon appIcon("resources/CashFlowIcon.png");
    if (appIcon.isNull()) {
        // Try alternative path
        appIcon = QIcon(":/resources/CashFlowIcon.png");
    }
    if (!appIcon.isNull()) {
        app.setWindowIcon(appIcon);
    }
    
    App w;
    w.show();
    return app.exec();
}
