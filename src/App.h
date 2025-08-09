#pragma once
#include <QMainWindow>
class MainWindow;
class QStatusBar;
class QLabel;
class App: public QMainWindow{
    Q_OBJECT
public:
    explicit App(QWidget*parent=nullptr);
    ~App();
private slots:
    void newDatabase();
    void openDatabase();
    void saveAsDatabase();
    void changeUser();
    void manageUsers();
    void about();
private:
    void loadDatabase(const QString& path);
    bool showLoginDialog();
    void updateStatusBar();
    MainWindow* m_main=nullptr;
    QStatusBar* m_statusBar=nullptr;
    QLabel* m_dbLabel=nullptr;
    QLabel* m_userLabel=nullptr;
    QString m_currentDbPath;
    QString m_currentUser;
};
