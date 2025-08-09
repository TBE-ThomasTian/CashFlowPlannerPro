#include "App.h"
#include "MainWindow.h"
#include "Database.h"
#include "LoginDialog.h"
#include <QFile>
#include <QApplication>
#include <QScreen>
#include <QSizeGrip>
#include <QStatusBar>
#include <QIcon>
#include <QMenuBar>
#include <QMenu>
#include <QAction>
#include <QLabel>
#include <QFileDialog>
#include <QMessageBox>
#include <QFileInfo>
#include <QInputDialog>
#include <QSqlQuery>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QPushButton>
#include <QPixmap>
#include <QDialog>
#include <QDateTime>
static void applyStyle(){
    // Use modern theme with cards
    qApp->setStyle("fusion");
    
    QFile f("resources/style_modern.qss");
    if(f.open(QIODevice::ReadOnly)) {
        qApp->setStyleSheet(QString::fromUtf8(f.readAll()));
        f.close();
    }
}

App::App(QWidget*parent):QMainWindow(parent){
    applyStyle();
    
    // DISABLE ALL EFFECTS - just basic rendering
    setAttribute(Qt::WA_OpaquePaintEvent, false);
    setAttribute(Qt::WA_NoSystemBackground, false);
    setAttribute(Qt::WA_PaintOnScreen, false);
    setAttribute(Qt::WA_StaticContents, false);
    setAutoFillBackground(false);
    
    // Create menu bar with modern styling
    auto *menuBar = this->menuBar();
    menuBar->setStyleSheet(
        "QMenuBar {"
        "    background-color: #f8f9fa;"
        "    border-bottom: 1px solid #dee2e6;"
        "    padding: 4px;"
        "    font-size: 13px;"
        "}"
        "QMenuBar::item {"
        "    padding: 6px 12px;"
        "    background-color: transparent;"
        "    border-radius: 4px;"
        "    margin: 0 2px;"
        "}"
        "QMenuBar::item:selected {"
        "    background-color: #e9ecef;"
        "}"
        "QMenuBar::item:pressed {"
        "    background-color: #dee2e6;"
        "}"
        "QMenu {"
        "    background-color: #ffffff;"
        "    border: 1px solid #dee2e6;"
        "    border-radius: 4px;"
        "    padding: 4px 0;"
        "}"
        "QMenu::item {"
        "    padding: 8px 24px;"
        "    font-size: 13px;"
        "}"
        "QMenu::item:selected {"
        "    background-color: #f8f9fa;"
        "}"
        "QMenu::separator {"
        "    height: 1px;"
        "    background-color: #e9ecef;"
        "    margin: 4px 0;"
        "}"
    );
    
    // File menu - only essential items
    auto *fileMenu = menuBar->addMenu("Datei");
    
    auto *openAction = fileMenu->addAction("🗂️  Datenbank wechseln...");
    openAction->setShortcut(QKeySequence::Open);
    connect(openAction, &QAction::triggered, this, &App::openDatabase);
    
    auto *backupAction = fileMenu->addAction("💾  Backup erstellen...");
    backupAction->setShortcut(QKeySequence::SaveAs);
    connect(backupAction, &QAction::triggered, this, &App::saveAsDatabase);
    
    fileMenu->addSeparator();
    
    auto *exitAction = fileMenu->addAction("🚪  Beenden");
    exitAction->setShortcut(QKeySequence::Quit);
    connect(exitAction, &QAction::triggered, this, &QWidget::close);
    
    // Help menu
    auto *helpMenu = menuBar->addMenu("Hilfe");
    auto *aboutAction = helpMenu->addAction("ℹ️  Über Cashflow Planer...");
    connect(aboutAction, &QAction::triggered, this, &App::about);
    
    // Add status bar with size grip
    m_statusBar = new QStatusBar(this);
    setStatusBar(m_statusBar);
    m_statusBar->setSizeGripEnabled(true);
    
    // Add user label to status bar
    m_userLabel = new QLabel("👤 Nicht angemeldet");
    m_statusBar->addWidget(m_userLabel);
    
    // Add database label to status bar
    m_dbLabel = new QLabel("Datenbank: cashflow.db");
    m_statusBar->addPermanentWidget(m_dbLabel);
    
    // Show unified login dialog (includes database selection)
    if (!showLoginDialog()) {
        // User cancelled login - exit the application
        QApplication::exit(0);
        exit(0);  // Force immediate exit
    }
    
    updateStatusBar();
    
    // Set up main widget after database is ready
    m_main=new MainWindow(this);
    setCentralWidget(m_main);
    setWindowTitle("Cashflow Planer - Finanzübersicht v1.0");
    setWindowIcon(QIcon("resources/CashFlowIcon.png"));
    resize(1200, 800);
} 

App::~App(){}

void App::newDatabase() {
    QString fileName = QFileDialog::getSaveFileName(this, 
        "Neue Datenbank erstellen", 
        "cashflow_neu.db",
        "SQLite Datenbank (*.db);;Alle Dateien (*)");
    
    if (!fileName.isEmpty()) {
        // Close current database
        Database::instance().close();
        
        // Delete file if it exists
        if (QFile::exists(fileName)) {
            QFile::remove(fileName);
        }
        
        // Open new database
        loadDatabase(fileName);
    }
}

void App::openDatabase() {
    QString fileName = QFileDialog::getOpenFileName(this, 
        "Datenbank wechseln", 
        "",
        "SQLite Datenbank (*.db);;Alle Dateien (*)");
    
    if (!fileName.isEmpty()) {
        loadDatabase(fileName);
    }
}

void App::saveAsDatabase() {
    QString fileName = QFileDialog::getSaveFileName(this, 
        "Backup erstellen", 
        "cashflow_backup_" + QDateTime::currentDateTime().toString("yyyyMMdd_HHmm") + ".db",
        "SQLite Datenbank (*.db);;Alle Dateien (*)");
    
    if (!fileName.isEmpty()) {
        // Copy current database to backup location
        if (QFile::exists(fileName)) {
            QFile::remove(fileName);
        }
        
        if (QFile::copy(m_currentDbPath, fileName)) {
            QMessageBox::information(this, "Backup erfolgreich", 
                "Backup wurde erstellt:\n" + QFileInfo(fileName).fileName());
        } else {
            QMessageBox::critical(this, "Fehler", 
                "Konnte Backup nicht erstellen!");
        }
    }
}

void App::loadDatabase(const QString& path) {
    // Close current database
    Database::instance().close();
    
    // Open new database
    if (Database::instance().open(path)) {
        Database::instance().ensureSchema();
        m_currentDbPath = path;
        updateStatusBar();
        
        // Refresh main window
        if (m_main) {
            delete m_main;
            m_main = new MainWindow(this);
            setCentralWidget(m_main);
        }
        
        QMessageBox::information(this, "Erfolg", 
            "Datenbank wurde geladen:\n" + QFileInfo(path).fileName());
    } else {
        QMessageBox::critical(this, "Fehler", 
            "Konnte Datenbank nicht öffnen!");
        // Try to reopen the previous database
        Database::instance().open(m_currentDbPath);
    }
}

void App::updateStatusBar() {
    if (m_dbLabel) {
        QFileInfo fi(m_currentDbPath);
        m_dbLabel->setText("📁 " + fi.fileName());
    }
    if (m_userLabel) {
        m_userLabel->setText("👤 " + m_currentUser);
    }
}

bool App::showLoginDialog() {
    LoginDialog dialog(nullptr);  // Use nullptr to make it independent
    
    // Center the dialog on screen
    if (QScreen *screen = QApplication::primaryScreen()) {
        QRect screenGeometry = screen->availableGeometry();
        int x = (screenGeometry.width() - dialog.width()) / 2;
        int y = (screenGeometry.height() - dialog.height()) / 2;
        dialog.move(x, y);
    }
    
    if (dialog.exec() == QDialog::Accepted) {
        m_currentUser = dialog.getUsername();
        m_currentDbPath = dialog.getDatabasePath();
        return true;
    }
    return false;
}

// Removed - now integrated into LoginDialog

void App::changeUser() {
    if (showLoginDialog()) {
        updateStatusBar();
        QMessageBox::information(this, "Anmeldung", 
            "Erfolgreich angemeldet als: " + m_currentUser);
    }
}

void App::manageUsers() {
    bool ok;
    QStringList items;
    items << "Passwort ändern" << "Benutzer löschen" << "Benutzerliste anzeigen";
    
    QString item = QInputDialog::getItem(this, "Benutzerverwaltung",
                                         "Was möchten Sie tun?", items, 0, false, &ok);
    if (!ok || item.isEmpty())
        return;
    
    if (item == "Passwort ändern") {
        QString newPassword = QInputDialog::getText(this, "Passwort ändern",
                                                    "Neues Passwort für " + m_currentUser + ":",
                                                    QLineEdit::Password, "", &ok);
        if (ok && !newPassword.isEmpty()) {
            QSqlQuery query(Database::instance().db());
            query.prepare("UPDATE users SET password_hash = ? WHERE username = ?");
            query.addBindValue(newPassword);
            query.addBindValue(m_currentUser);
            
            if (query.exec()) {
                QMessageBox::information(this, "Erfolg", "Passwort wurde geändert!");
            } else {
                QMessageBox::critical(this, "Fehler", "Passwort konnte nicht geändert werden!");
            }
        }
    } else if (item == "Benutzerliste anzeigen") {
        QSqlQuery query(Database::instance().db());
        query.exec("SELECT username, full_name, created_at FROM users ORDER BY username");
        
        QString userList = "<h3>Registrierte Benutzer:</h3><ul>";
        while (query.next()) {
            userList += "<li><b>" + query.value(0).toString() + "</b>";
            if (!query.value(1).toString().isEmpty()) {
                userList += " (" + query.value(1).toString() + ")";
            }
            userList += " - Erstellt: " + query.value(2).toString() + "</li>";
        }
        userList += "</ul>";
        
        QMessageBox::information(this, "Benutzerliste", userList);
    }
}

void App::about() {
    QMessageBox::about(this, "Über Cashflow Planer",
        "<h2>Cashflow Planer</h2>"
        "<p><b>Version 1.0</b></p>"
        "<p>Build: " __DATE__ " " __TIME__ "</p>"
        "<p>Ein Tool zur Verwaltung Ihrer Finanzen und Cashflow-Prognosen.</p>"
        "<p><b>Features:</b></p>"
        "<ul>"
        "<li>Multi-User Support mit Login-System</li>"
        "<li>Mehrere Datenbanken für verschiedene Zwecke</li>"
        "<li>Teilen Sie Datenbanken mit anderen Personen</li>"
        "<li>Cashflow-Prognose und Finanzplanung</li>"
        "<li>Steuerverwaltung und Fixkosten-Tracking</li>"
        "</ul>"
        "<p><b>Aktuell angemeldet als:</b> " + m_currentUser + "</p>"
        "<p><i>© 2024 - Entwickelt mit Qt " QT_VERSION_STR "</i></p>");
}
